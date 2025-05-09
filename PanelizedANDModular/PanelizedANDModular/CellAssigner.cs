using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PanelizedAndModularFinal
{
    /// <summary>
    /// Info about one cell you filled for a space.
    /// </summary>
    public class CellFillInfo
    {
        public ModuleGridCell Cell { get; set; }
        public double CellArea { get; set; }
        public double OverlapArea { get; set; }
        public double ExtraArea => CellArea - OverlapArea;
        public ElementId RegionId { get; set; }
    }

    /// <summary>
    /// Results of filling one space: 
    /// which elements were created and the per-cell metrics.
    /// </summary>
    public class FillResult
    {
        public List<ElementId> RegionIds { get; } = new List<ElementId>();
        public List<CellFillInfo> CellInfos { get; } = new List<CellFillInfo>();
        public double TotalOverlapArea => CellInfos.Sum(i => i.OverlapArea);
        public double TotalExtraArea => CellInfos.Sum(i => i.ExtraArea);
    }

    public class CellAssigner
    {
        private readonly Document _doc;
        private readonly View _view;
        private readonly FilledRegionType _regionType;
        private readonly FillPatternElement _fillPattern;

        public CellAssigner(Document doc, View view)
        {
            _doc = doc;
            _view = view;
            _regionType = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .First();
            _fillPattern = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .First(fp =>
                    fp.GetFillPattern().IsSolidFill &&
                    fp.GetFillPattern().Target == FillPatternTarget.Drafting
                );
        }

        public List<ElementId> Phase0ResolveMultiOverlaps(List<ModuleGridCell> cells)
        {
            var regionIds = new List<ElementId>();
            double tol = _doc.Application.ShortCurveTolerance;
            var claimedAreas = new List<UVRect>();

            using (var tx = new Transaction(_doc, "Phase 0: Multi-Room Overlap - Area-Based"))
            {
                tx.Start();

                // 1) sort rooms by descending budget
                var roomsBy = GlobalData.SavedSpaces
                    .OrderByDescending(sp => sp.SquareTrimmedArea)
                    .ToList();

                foreach (var winner in roomsBy)
                {
                    // build loser map
                    var overlapMap = GlobalData.SavedSpaces
                        .Where(sp => sp != winner)
                        .ToDictionary(sp => sp, sp => new List<(ModuleGridCell cell, UVRect overlap)>());

                    // collect per-cell overlaps not already claimed
                    foreach (var cell in cells)
                    {
                        var wOpt = ComputeIntersection(cell, winner);
                        if (!wOpt.HasValue) continue;
                        var wRect = wOpt.Value;

                        foreach (var loser in overlapMap.Keys.ToList())
                        {
                            var lOpt = ComputeIntersection(cell, loser);
                            if (!lOpt.HasValue) continue;
                            var rect = wRect.Intersect(lOpt.Value);
                            if (rect.W <= tol || rect.H <= tol) continue;
                            if (IsClaimed(rect, claimedAreas)) continue;
                            overlapMap[loser].Add((cell, rect));
                        }
                    }

                    // summarize and sort losers
                    var loserGroups = overlapMap
                        .Select(kvp => new { Loser = kvp.Key, Total = kvp.Value.Sum(p => p.overlap.Area) })
                        .Where(x => x.Total > tol)
                        .OrderByDescending(x => x.Total)
                        .ToList();

                    // assign each loser
                    foreach (var grp in loserGroups)
                    {
                        var loser = grp.Loser;
                        double winB = winner.SquareTrimmedArea;
                        double losB = loser.SquareTrimmedArea;

                        var fullWin = FullRoomRect(winner);
                        var fullLos = FullRoomRect(loser);
                        var sqOverlap = fullWin.Intersect(fullLos);
                        if (sqOverlap.W <= tol || sqOverlap.H <= tol) continue;
                        if (IsClaimed(sqOverlap, claimedAreas)) continue;

                        bool winnerWins = winB > losB || (winB == losB && grp.Total >= sqOverlap.Area);
                        var owner = winnerWins ? winner : loser;
                        var other = winnerWins ? loser : winner;

                        // claim overlap per cell
                        foreach (var cell in cells)
                        {
                            var cellRect = new UVRect(cell.OriginX, cell.OriginY, cell.Size, cell.Size);
                            var patch = sqOverlap.Intersect(cellRect);
                            if (patch.W <= tol || patch.H <= tol) continue;
                            if (IsClaimed(patch, claimedAreas)) continue;

                            var loop = patch.ToCurveLoop();
                            var reg = FilledRegion.Create(_doc, _regionType.Id, _view.Id, new List<CurveLoop> { loop });
                            _view.SetElementOverrides(reg.Id, MakeOGS(owner).SetSurfaceTransparency(0));
                            regionIds.Add(reg.Id);
                            claimedAreas.Add(patch);
                        }

                        // carve remainder per cell
                        var carveRect = winnerWins ? fullLos : fullWin;
                        foreach (var strip in SubtractRectangles(carveRect, sqOverlap))
                        {
                            if (strip.W <= tol || strip.H <= tol) continue;
                            foreach (var cell in cells)
                            {
                                var cellRect = new UVRect(cell.OriginX, cell.OriginY, cell.Size, cell.Size);
                                var piece = strip.Intersect(cellRect);
                                if (piece.W <= tol || piece.H <= tol) continue;
                                if (IsClaimed(piece, claimedAreas)) continue;

                                var loop = piece.ToCurveLoop();
                                var reg = FilledRegion.Create(_doc, _regionType.Id, _view.Id, new List<CurveLoop> { loop });
                                _view.SetElementOverrides(reg.Id, MakeOGS(other).SetSurfaceTransparency(0));
                                regionIds.Add(reg.Id);
                                claimedAreas.Add(piece);
                            }
                        }

                        // update budgets
                        double area = sqOverlap.Area;
                        if (winnerWins)
                        {
                            winner.SquareTrimmedArea -= area;
                            loser.SquareTrimmedArea += area;
                        }
                        else
                        {
                            loser.SquareTrimmedArea -= area;
                            winner.SquareTrimmedArea += area;
                        }
                    }
                }
                tx.Commit();
            }
            return regionIds;
        }

        // returns the full room square
        private UVRect FullRoomRect(SpaceNode sp)
        {
            double r = Math.Sqrt(sp.Area / Math.PI);
            return new UVRect(sp.Position.X - r, sp.Position.Y - r, 2 * r, 2 * r);
        }

        // checks if rect center lies in any claimed
        private bool IsClaimed(UVRect r, List<UVRect> claimed)
        {
            double cx = r.X + r.W / 2;
            double cy = r.Y + r.H / 2;
            return claimed.Any(c => cx >= c.X && cx <= c.X + c.W
                                    && cy >= c.Y && cy <= c.Y + c.H);
        }


























        // ------------------- helpers -------------------

        // Compute the axis‑aligned intersection of a cell and a space’s square.
        // Returns null if no overlap.
        private UVRect? ComputeIntersection(ModuleGridCell cell, SpaceNode sp)
        {
            double c0x = cell.OriginX, c0y = cell.OriginY;
            double c1x = c0x + cell.Size, c1y = c0y + cell.Size;
            double r = Math.Sqrt(sp.Area / Math.PI);
            double s0x = sp.Position.X - r, s0y = sp.Position.Y - r;
            double s1x = s0x + 2 * r, s1y = s0y + 2 * r;
            double ix0 = Math.Max(c0x, s0x), iy0 = Math.Max(c0y, s0y);
            double ix1 = Math.Min(c1x, s1x), iy1 = Math.Min(c1y, s1y);
            if (ix1 <= ix0 || iy1 <= iy0) return null;
            return new UVRect(ix0, iy0, ix1 - ix0, iy1 - iy0);
        }

        // Generate all length‑k combinations from a list.
        private IEnumerable<List<T>> Combinations<T>(List<T> list, int k)
        {
            if (k == 0) yield return new List<T>();
            else
            {
                for (int i = 0; i <= list.Count - k; i++)
                {
                    foreach (var tail in Combinations(list.Skip(i + 1).ToList(), k - 1))
                    {
                        var comb = new List<T> { list[i] };
                        comb.AddRange(tail);
                        yield return comb;
                    }
                }
            }
        }

        // Build the OverrideGraphicSettings for a given space.
        private OverrideGraphicSettings MakeOGS(SpaceNode sp)
        {
            return new OverrideGraphicSettings()
                .SetSurfaceForegroundPatternColor(new Color(sp.WpfColor.R, sp.WpfColor.G, sp.WpfColor.B))
                .SetSurfaceBackgroundPatternColor(new Color(sp.WpfColor.R, sp.WpfColor.G, sp.WpfColor.B))
                .SetSurfaceForegroundPatternId(_fillPattern.Id)
                .SetSurfaceBackgroundPatternId(_fillPattern.Id)
                .SetSurfaceTransparency(50)
                .SetProjectionLineColor(new Color(sp.WpfColor.R, sp.WpfColor.G, sp.WpfColor.B))
                .SetProjectionLineWeight(5);
        }

        // Simple struct for axis‑aligned rectangles in UV space
        private struct UVRect
        {
            public double X, Y, W, H;
            public double Area => W * H;
            public UVRect(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }
            public UVRect Intersect(UVRect o)
               => new UVRect(Math.Max(X, o.X), Math.Max(Y, o.Y),
                             Math.Max(0, Math.Min(X + W, o.X + o.W) - Math.Max(X, o.X)),
                             Math.Max(0, Math.Min(Y + H, o.Y + o.H) - Math.Max(Y, o.Y)));
            public CurveLoop ToCurveLoop()
            {
                var loop = new CurveLoop();
                var p0 = new XYZ(X, Y, 0);
                var p1 = new XYZ(X + W, Y, 0);
                var p2 = new XYZ(X + W, Y + H, 0);
                var p3 = new XYZ(X, Y + H, 0);
                loop.Append(Line.CreateBound(p0, p1));
                loop.Append(Line.CreateBound(p1, p2));
                loop.Append(Line.CreateBound(p2, p3));
                loop.Append(Line.CreateBound(p3, p0));
                return loop;
            }


        }
        // Returns the list of sub‐rectangles when you cut `inner` out of `outer`
        private List<UVRect> SubtractRectangles(UVRect outer, UVRect inner)
        {
            var results = new List<UVRect>();
            // left strip
            if (inner.X > outer.X)
                results.Add(new UVRect(outer.X, outer.Y,
                                       inner.X - outer.X, outer.H));
            // right strip
            if (inner.X + inner.W < outer.X + outer.W)
                results.Add(new UVRect(inner.X + inner.W, outer.Y,
                                       (outer.X + outer.W) - (inner.X + inner.W), outer.H));
            // bottom strip
            if (inner.Y > outer.Y)
                results.Add(new UVRect(Math.Max(outer.X, inner.X),
                                        outer.Y,
                                        Math.Min(outer.W, inner.W),
                                        inner.Y - outer.Y));
            // top strip
            if (inner.Y + inner.H < outer.Y + outer.H)
                results.Add(new UVRect(Math.Max(outer.X, inner.X),
                                        inner.Y + inner.H,
                                        Math.Min(outer.W, inner.W),
                                        (outer.Y + outer.H) - (inner.Y + inner.H)));
            return results.Where(r => r.W > 0 && r.H > 0).ToList();
        }
























        /// <summary>
        /// Fills module cells overlapping exactly one space (this space),
        /// ordered by descending overlap area, with one allowed overdraft.
        /// </summary>
        public FillResult FillOverlappingCells(
            List<ModuleGridCell> cells,
            SpaceNode space)
        {
            // compute this space's square bounds
            double radius = Math.Sqrt(space.Area / Math.PI);
            double side = 2 * radius;
            double minX = space.Position.X - radius;
            double minY = space.Position.Y - radius;
            double maxX = minX + side;
            double maxY = minY + side;

            var result = new FillResult();
            double remaining = space.SquareTrimmedArea;
            bool usedOverdraft = false;

            // collect only cells overlapped by exactly this one space
            var candidates = new List<(ModuleGridCell cell, double overlapArea, double cellArea, double extraArea)>();
            foreach (var cell in cells)
            {
                double cminX = cell.OriginX;
                double cminY = cell.OriginY;
                double cmaxX = cminX + cell.Size;
                double cmaxY = cminY + cell.Size;

                // count overlapping spaces
                int overlapCount = 0;
                foreach (var sp in GlobalData.SavedSpaces)
                {
                    double sr = Math.Sqrt(sp.Area / Math.PI);
                    double ss = 2 * sr;
                    double smx = sp.Position.X - sr;
                    double smy = sp.Position.Y - sr;
                    double sMx = smx + ss;
                    double sMy = smy + ss;
                    double ox = Math.Max(0, Math.Min(cmaxX, sMx) - Math.Max(cminX, smx));
                    double oy = Math.Max(0, Math.Min(cmaxY, sMy) - Math.Max(cminY, smy));
                    if (ox > 0 && oy > 0)
                    {
                        overlapCount++;
                        if (overlapCount > 1) break;
                    }
                }
                if (overlapCount != 1) continue;

                // compute overlap with this space
                double overlapX = Math.Max(0, Math.Min(cmaxX, maxX) - Math.Max(cminX, minX));
                double overlapY = Math.Max(0, Math.Min(cmaxY, maxY) - Math.Max(cminY, minY));
                if (overlapX <= 0 || overlapY <= 0) continue;

                double overlapArea = overlapX * overlapY;
                double cellArea = cell.Size * cell.Size;
                double extraArea = cellArea - overlapArea;

                candidates.Add((cell, overlapArea, cellArea, extraArea));
            }

            // sort candidates by largest overlap first
            var sorted = candidates.OrderByDescending(c => c.overlapArea).ToList();

            using (var tx = new Transaction(_doc, "Fill Ordered Cells"))
            {
                tx.Start();

                var ogs = new OverrideGraphicSettings()
                    .SetSurfaceForegroundPatternColor(new Color(space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                    .SetSurfaceBackgroundPatternColor(new Color(space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                    .SetSurfaceForegroundPatternId(_fillPattern.Id)
                    .SetSurfaceBackgroundPatternId(_fillPattern.Id)
                    .SetSurfaceTransparency(50)
                    .SetProjectionLineColor(new Color(space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                    .SetProjectionLineWeight(5);

                foreach (var entry in sorted)
                {
                    var cell = entry.cell;
                    var overlapArea = entry.overlapArea;
                    var cellArea = entry.cellArea;
                    var extraArea = entry.extraArea;

                    // full-cell fill while budget allows
                    if (remaining > 0)
                    {
                        var region = FilledRegion.Create(
                            _doc, _regionType.Id, _view.Id,
                            new List<CurveLoop> { cell.Loop });
                        _view.SetElementOverrides(region.Id, ogs);
                        result.RegionIds.Add(region.Id);
                        result.CellInfos.Add(new CellFillInfo
                        {
                            Cell = cell,
                            CellArea = cellArea,
                            OverlapArea = overlapArea,
                            RegionId = region.Id
                        });
                        remaining -= extraArea;
                        space.SquareTrimmedArea = remaining;
                        if (remaining <= 0) usedOverdraft = true;
                    }
                    // one overdraft: partial overlap fill then stop
                    else if (usedOverdraft)
                    {
                        var loop = new CurveLoop();
                        double ix0 = Math.Max(cell.OriginX, minX);
                        double iy0 = Math.Max(cell.OriginY, minY);
                        double ix1 = Math.Min(cell.OriginX + cell.Size, maxX);
                        double iy1 = Math.Min(cell.OriginY + cell.Size, maxY);
                        loop.Append(Line.CreateBound(new XYZ(ix0, iy0, 0), new XYZ(ix1, iy0, 0)));
                        loop.Append(Line.CreateBound(new XYZ(ix1, iy0, 0), new XYZ(ix1, iy1, 0)));
                        loop.Append(Line.CreateBound(new XYZ(ix1, iy1, 0), new XYZ(ix0, iy1, 0)));
                        loop.Append(Line.CreateBound(new XYZ(ix0, iy1, 0), new XYZ(ix0, iy0, 0)));
                        var region = FilledRegion.Create(
                            _doc, _regionType.Id, _view.Id,
                            new List<CurveLoop> { loop });
                        _view.SetElementOverrides(region.Id, ogs);
                        result.RegionIds.Add(region.Id);
                        result.CellInfos.Add(new CellFillInfo
                        {
                            Cell = cell,
                            CellArea = cellArea,
                            OverlapArea = overlapArea,
                            RegionId = region.Id
                        });
                        break;
                    }
                }

                tx.Commit();
            }

            return result;
        }



        /// <summary>
        /// Phase 2: Assign contested cells in descending‐budget order:
        /// repeatedly pick the space with the highest remaining trimmed area,
        /// give it any cell it contests where it still leads the budget,
        /// update all budgets, and remove that cell from contention.
        /// </summary>
        public List<ElementId> ResolveContestedCells(List<ModuleGridCell> cells)
        {
            var regionIds = new List<ElementId>();

            // 1) Build map of only the cells overlapped by >=2 spaces
            var contested = new Dictionary<ModuleGridCell, List<(SpaceNode space, double overlapArea)>>();
            foreach (var cell in cells)
            {
                double x0 = cell.OriginX, y0 = cell.OriginY;
                double x1 = x0 + cell.Size, y1 = y0 + cell.Size;
                var overlaps = new List<(SpaceNode, double)>();
                foreach (var sp in GlobalData.SavedSpaces)
                {
                    double r = Math.Sqrt(sp.Area / Math.PI);
                    double sx0 = sp.Position.X - r, sy0 = sp.Position.Y - r;
                    double sx1 = sx0 + 2 * r, sy1 = sy0 + 2 * r;
                    double ox = Math.Max(0, Math.Min(x1, sx1) - Math.Max(x0, sx0));
                    double oy = Math.Max(0, Math.Min(y1, sy1) - Math.Max(y0, sy0));
                    if (ox > 0 && oy > 0)
                        overlaps.Add((sp, ox * oy));
                }
                if (overlaps.Count >= 2)
                    contested[cell] = overlaps;
            }

            using (var tx = new Transaction(_doc, "Resolve Contested Cells by Budget"))
            {
                tx.Start();

                // 2) While any contested remain:
                while (contested.Count > 0)
                {
                    bool didAssign = false;

                    // sort spaces by descending budget
                    var spacesByBudget = GlobalData.SavedSpaces
                        .OrderByDescending(s => s.SquareTrimmedArea)
                        .ToList();

                    foreach (var space in spacesByBudget)
                    {
                        // cells this space still contests
                        var myCells = contested
                            .Where(kv => kv.Value.Any(o => o.space == space))
                            .ToList();

                        foreach (var kv in myCells)
                        {
                            var cell = kv.Key;
                            var overlaps = kv.Value;

                            // only assign if this space leads all others here
                            if (overlaps.All(o => space.SquareTrimmedArea >= o.space.SquareTrimmedArea))
                            {
                                // compute extra area
                                double cellArea = cell.Size * cell.Size;
                                double overlapArea = overlaps.First(o => o.space == space).overlapArea;
                                double extraArea = cellArea - overlapArea;

                                // draw full‐cell for winner
                                var region = FilledRegion.Create(
                                    _doc, _regionType.Id, _view.Id,
                                    new List<CurveLoop> { cell.Loop });
                                var ogs = new OverrideGraphicSettings()
                                    .SetSurfaceForegroundPatternColor(new Color(
                                        space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                                    .SetSurfaceBackgroundPatternColor(new Color(
                                        space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                                    .SetSurfaceForegroundPatternId(_fillPattern.Id)
                                    .SetSurfaceBackgroundPatternId(_fillPattern.Id)
                                    .SetSurfaceTransparency(50)
                                    .SetProjectionLineColor(new Color(
                                        space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                                    .SetProjectionLineWeight(5);
                                _view.SetElementOverrides(region.Id, ogs);
                                regionIds.Add(region.Id);

                                // update budgets
                                space.SquareTrimmedArea -= extraArea;
                                foreach (var loser in overlaps.Where(o => o.space != space))
                                    loser.space.SquareTrimmedArea += loser.overlapArea;

                                // remove cell from further contest
                                contested.Remove(cell);
                                didAssign = true;
                                break;
                            }
                        }
                        if (didAssign) break;
                    }

                    if (!didAssign)
                        break; // no further assignments possible
                }

                tx.Commit();
            }

            return regionIds;
        }




    }


}
