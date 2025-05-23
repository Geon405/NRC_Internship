using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
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
        public List<ElementId> RegionIds { get; } = new List<ElementId>();
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










        public struct UVRect
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






















































        ///////////////////////////////////////////////////////////////////////////PHASE 0 ///////////////////////////////////////////////////////////////////////////
        ///////////////////////////////////////////////////////////////////////////PHASE 0 ///////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////PHASE 0 ///////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////PHASE 0 ///////////////////////////////////////////////////////////////////////////






     

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










        /// <summary>
        /// PHASE 0: dynamically re-sort after each room is resolved,
        /// only losers gain trimmed area when they lose an overlap.
        /// </summary>
        public List<ElementId> Phase0ResolveMultiOverlaps(
            IList<ModuleGridCell> cells,
            IEnumerable<GridTrimmer.TrimResult> trims,
            FillPatternElement fillPatternOverride)
        {
            var allRegionIds = new List<ElementId>();

            // 0) first re-snap trimmed loops into cells
            var trimmedIds = ResnapTrimmedLoopsIntoCells(cells, trims, fillPatternOverride);
            allRegionIds.AddRange(trimmedIds);

            double tol = _doc.Application.ShortCurveTolerance;
            var claimedAreas = new List<UVRect>();
            var unresolved = new List<SpaceNode>(GlobalData.SavedSpaces);

            using (var tx = new Transaction(_doc, "Phase 0: Multi-Room Overlap"))
            {
                tx.Start();

                while (unresolved.Any())
                {
                    // sort by highest budget
                    unresolved = unresolved
                        .OrderByDescending(sp => sp.SquareTrimmedArea)
                        .ToList();
                    var winner = unresolved.First();
                    unresolved.RemoveAt(0);

                    // build overlap map
                    var overlapMap = unresolved
                        .ToDictionary(sp => sp, sp => new List<(ModuleGridCell cell, UVRect overlap)>());

                    // collect overlaps per cell
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

                    // sort losers by total overlap
                    var loserGroups = overlapMap
                        .Select(kvp => new { Loser = kvp.Key, Total = kvp.Value.Sum(p => p.overlap.Area) })
                        .Where(x => x.Total > tol)
                        .OrderByDescending(x => x.Total)
                        .ToList();

                    // resolve each
                    foreach (var grp in loserGroups)
                    {
                        var loser = grp.Loser;
                        var fullWin = FullRoomRect(winner);
                        var fullLos = FullRoomRect(loser);
                        var sqOverlap = fullWin.Intersect(fullLos);
                        if (sqOverlap.W <= tol || sqOverlap.H <= tol) continue;
                        if (IsClaimed(sqOverlap, claimedAreas)) continue;

                        bool winnerWins =
                            winner.SquareTrimmedArea > loser.SquareTrimmedArea
                            || (winner.SquareTrimmedArea == loser.SquareTrimmedArea
                                && grp.Total >= sqOverlap.Area);
                        var owner = winnerWins ? winner : loser;
                        var other = winnerWins ? loser : winner;

                        // draw overlap patch
                        foreach (var cell in cells)
                        {
                            var cellRect = new UVRect(cell.OriginX, cell.OriginY, cell.Size, cell.Size);
                            var patch = sqOverlap.Intersect(cellRect);
                            if (patch.W <= tol || patch.H <= tol) continue;
                            if (IsClaimed(patch, claimedAreas)) continue;

                            var reg = FilledRegion.Create(
                                _doc, _regionType.Id, _view.Id,
                                new[] { patch.ToCurveLoop() });
                            _view.SetElementOverrides(reg.Id,
                                MakeOGS(owner).SetSurfaceTransparency(0));
                            allRegionIds.Add(reg.Id);
                            cell.RegionIds.Add(reg.Id);
                            claimedAreas.Add(patch);
                        }

                        // carve remainder
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

                                var reg = FilledRegion.Create(
                                    _doc, _regionType.Id, _view.Id,
                                    new[] { piece.ToCurveLoop() });
                                _view.SetElementOverrides(reg.Id,
                                    MakeOGS(other).SetSurfaceTransparency(0));
                                allRegionIds.Add(reg.Id);
                                cell.RegionIds.Add(reg.Id);
                                claimedAreas.Add(piece);
                            }
                        }

                        // update budgets: always credit the actual loser of this overlap
                        double area = sqOverlap.Area;
                        other.SquareTrimmedArea += area;
                    }
                }

                tx.Commit();
            }

            return allRegionIds;
        }






































        ///////////////////////////////////////////////////////////////////////////PHASE 1 ///////////////////////////////////////////////////////////////////////////
        ///////////////////////////////////////////////////////////////////////////PHASE 1 ///////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////PHASE 1 ///////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////PHASE 1 ///////////////////////////////////////////////////////////////////////////





        /// <summary>
        /// PHASE 1: Clear or fully fill every partially-filled cell per room,
        /// expanding only through adjacency and then handling any leftovers
        /// by filling if budget remains or clearing otherwise.
        /// Operates on cells already loaded with Phase 0 regions in cell.RegionIds.
        /// </summary>
        public List<ElementId> Phase1ResolveSingleOverlap(List<ModuleGridCell> cells)
        {
            var deletedOrAddedIds = new List<ElementId>();
            double tol = _doc.Application.ShortCurveTolerance;

            // Process rooms by descending remaining trimmed area
            var rooms = GlobalData.SavedSpaces
                .OrderByDescending(sp => sp.SquareTrimmedArea)
                .ToList();

            using (var tx = new Transaction(_doc, "Phase 1: Fill & Clear Single-Overlap Cells"))
            {
                tx.Start();

                foreach (var space in rooms)
                {
                    // 1) identify partial cells: exclusively this room, partial overlap
                    var partials = new List<(ModuleGridCell cell, double overlap)>();
                    foreach (var cell in cells)
                    {
                        int count = 0;
                        double overlapArea = 0;
                        double cellArea = cell.Size * cell.Size;

                        foreach (var sp in GlobalData.SavedSpaces)
                        {
                            var intr = ComputeIntersection(cell, sp);
                            if (!intr.HasValue) continue;
                            count++;
                            if (sp == space)
                                overlapArea = intr.Value.Area;
                            if (count > 1) break;
                        }

                        if (count == 1 && overlapArea > tol && overlapArea < cellArea)
                            partials.Add((cell, overlapArea));
                    }

                    // 2) sort candidates by descending overlap
                    var toProcess = partials.OrderByDescending(p => p.overlap).ToList();
                    var skipped = new List<(ModuleGridCell cell, double overlap)>();
                    var filled = new HashSet<ModuleGridCell>();

                    // 3) first pass: fill best overlaps, seeding with any cell
                    foreach (var (cell, overlap) in toProcess)
                    {
                        double cellArea = cell.Size * cell.Size;
                        if (space.SquareTrimmedArea > 0 &&
                            (filled.Count == 0 || IsAdjacent(cell, filled)))
                        {
                            var reg = FilledRegion.Create(
                                _doc, _regionType.Id, _view.Id,
                                new List<CurveLoop> { cell.Loop });
                            _view.SetElementOverrides(reg.Id, MakeOGS(space).SetSurfaceTransparency(0));
                            cell.RegionIds.Add(reg.Id);
                            deletedOrAddedIds.Add(reg.Id);

                            filled.Add(cell);
                            space.SquareTrimmedArea -= (cellArea - overlap);
                        }
                        else
                        {
                            skipped.Add((cell, overlap));
                        }
                    }

                    // 4) adjacency passes: keep filling as long as budget and adjacency allow
                    bool didFill;
                    do
                    {
                        didFill = false;
                        foreach (var entry in skipped.ToList())
                        {
                            var cell = entry.cell;
                            var overlap = entry.overlap;
                            double cellArea = cell.Size * cell.Size;

                            if (space.SquareTrimmedArea > 0 && IsAdjacent(cell, filled))
                            {
                                var reg = FilledRegion.Create(
                                    _doc, _regionType.Id, _view.Id,
                                    new List<CurveLoop> { cell.Loop });
                                _view.SetElementOverrides(reg.Id, MakeOGS(space).SetSurfaceTransparency(0));
                                cell.RegionIds.Add(reg.Id);
                                deletedOrAddedIds.Add(reg.Id);

                                filled.Add(cell);
                                space.SquareTrimmedArea -= (cellArea - overlap);
                                skipped.Remove(entry);
                                didFill = true;
                                break;
                            }
                        }
                    } while (didFill);

                    // 5) final pass: handle any leftover partials
                    foreach (var entry in skipped.OrderByDescending(p => p.overlap))
                    {
                        var cell = entry.cell;
                        var overlap = entry.overlap;
                        double cellArea = cell.Size * cell.Size;

                        if (space.SquareTrimmedArea > 0)
                        {
                            // fill remainder of this cell
                            var reg = FilledRegion.Create(
                                _doc, _regionType.Id, _view.Id,
                                new List<CurveLoop> { cell.Loop });
                            _view.SetElementOverrides(reg.Id, MakeOGS(space).SetSurfaceTransparency(0));
                            cell.RegionIds.Add(reg.Id);
                            deletedOrAddedIds.Add(reg.Id);

                            space.SquareTrimmedArea -= (cellArea - overlap);
                        }
                        else
                        {
                            // clear everything in this cell
                            foreach (var id in cell.RegionIds)
                            {
                                deletedOrAddedIds.Add(id);
                                _doc.Delete(id);
                            }
                            cell.RegionIds.Clear();
                        }
                    }
                }

                tx.Commit();
            }

            return deletedOrAddedIds;
        }







        /// <summary>
        /// True if 'cell' shares an edge with any in 'filled'.
        /// </summary>
        private bool IsAdjacent(ModuleGridCell cell, HashSet<ModuleGridCell> filled)
        {
            return filled.Any(n =>
                (n.OriginX == cell.OriginX + cell.Size && n.OriginY == cell.OriginY) ||
                (n.OriginX == cell.OriginX - cell.Size && n.OriginY == cell.OriginY) ||
                (n.OriginX == cell.OriginX && n.OriginY == cell.OriginY + cell.Size) ||
                (n.OriginX == cell.OriginX && n.OriginY == cell.OriginY - cell.Size)
            );
        }




        ///// <summary>
        ///// Remove only the Phase-1 full-cell fills for `space` in exactly this cell.
        ///// </summary>
        //private void ClearCell(ModuleGridCell cell, SpaceNode space)
        //{
        //    double tol = _doc.Application.ShortCurveTolerance;
        //    var cellRect = new UVRect(cell.OriginX, cell.OriginY, cell.Size, cell.Size);

        //    // find ONLY the FilledRegions whose bbox exactly matches the cell
        //    // AND whose color matches this space
        //    var toDelete = new FilteredElementCollector(_doc, _view.Id)
        //        .OfClass(typeof(FilledRegion))
        //        .Cast<FilledRegion>()
        //        .Where(fr =>
        //        {
        //            var bb = fr.get_BoundingBox(_view);
        //            if (bb == null) return false;
        //            // exact full-cell bbox?
        //            bool full =
        //                Math.Abs(bb.Min.X - cellRect.X) < tol &&
        //                Math.Abs(bb.Min.Y - cellRect.Y) < tol &&
        //                Math.Abs(bb.Max.X - (cellRect.X + cellRect.W)) < tol &&
        //                Math.Abs(bb.Max.Y - (cellRect.Y + cellRect.H)) < tol;
        //            if (!full) return false;
        //            // same color?
        //            var ogs = _view.GetElementOverrides(fr.Id);
        //            var c = ogs.ProjectionLineColor;
        //            return c.Red == space.WpfColor.R
        //                && c.Green == space.WpfColor.G
        //                && c.Blue == space.WpfColor.B;
        //        })
        //        .Select(fr => fr.Id)
        //        .ToList();

        //    foreach (var id in toDelete)
        //        _doc.Delete(id);
        //}










        ///////////////////////////////////////////////////////////////////////////PHASE 2 ///////////////////////////////////////////////////////////////////////////
        ///////////////////////////////////////////////////////////////////////////PHASE 2 ///////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////PHASE 2 ///////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////PHASE 2 ///////////////////////////////////////////////////////////////////////////





        /// <summary>
        /// PHASE 2 
        /// </summary>
        public List<ElementId> Phase2ResolveContestedCells(List<ModuleGridCell> cells)
        {


            var regionIds = new List<ElementId>();
            var cellAssignment = new Dictionary<ModuleGridCell, SpaceNode>();
            var cellRegionMap = new Dictionary<ModuleGridCell, ElementId>();
            var assignedCells = new HashSet<ModuleGridCell>();
            double tol = _doc.Application.ShortCurveTolerance;

            // 1) Identify only truly contested cells
            var contested = new Dictionary<ModuleGridCell, List<(SpaceNode space, double overlapArea)>>();
            foreach (var cell in cells)
            {
                // 1a) If there's a top region that already fills the entire cell, skip it
                if (cell.RegionIds.Any())
                {
                    var topId = cell.RegionIds.Last();
                    if (_doc.GetElement(topId) is FilledRegion topReg)
                    {
                        var bb = topReg.get_BoundingBox(_view);
                        if (bb != null
                            && Math.Abs(bb.Min.X - cell.OriginX) < tol
                            && Math.Abs(bb.Min.Y - cell.OriginY) < tol
                            && Math.Abs(bb.Max.X - (cell.OriginX + cell.Size)) < tol
                            && Math.Abs(bb.Max.Y - (cell.OriginY + cell.Size)) < tol)
                        {
                            // this region covers the full cell → nothing left to contest
                            continue;
                        }
                    }
                }

                // 1b) Compute geometric overlaps
                var overlaps = new List<(SpaceNode space, double overlapArea)>();
                foreach (var sp in GlobalData.SavedSpaces)
                {
                    var intr = ComputeIntersection(cell, sp);
                    if (intr.HasValue)
                        overlaps.Add((sp, intr.Value.Area));
                }

                // 1c) Only keep cells with ≥2 distinct overlapping spaces
                if (overlaps.Select(o => o.space).Distinct().Count() >= 2)
                    contested[cell] = overlaps;
            }

            // Preserve these for the connectivity step
            var originalOverlaps = contested
                .ToDictionary(kv => kv.Key,
                              kv => new List<(SpaceNode space, double overlapArea)>(kv.Value));

            using (var tx = new Transaction(_doc, "Resolve Contested Cells (50% + Connectivity)"))
            {
                tx.Start();

                // 2) Assign contested cells by 50% rule or highest budget
                while (contested.Count > 0)
                {
                    bool didAssign = false;

                    // 2a) 50% takeover
                    // 2a) Modified 50% takeover with coverage fallback
                    foreach (var kv in contested.ToList())
                    {
                        var cell = kv.Key;
                        var overlaps = kv.Value;
                        double area = cell.Size * cell.Size;

                        // detect any >50% occupant
                        var topOverlap = overlaps.FirstOrDefault(o => o.overlapArea > 0.5 * area);
                        if (topOverlap.space != null)
                        {
                            SpaceNode winner;

                            if (topOverlap.space.SquareTrimmedArea > 0)
                            {
                                // primary room still needs area
                                winner = topOverlap.space;
                            }
                            else
                            {
                                // try rival with highest positive budget
                                winner = overlaps
                                    .Where(o => o.space.SquareTrimmedArea > 0)
                                    .OrderByDescending(o => o.space.SquareTrimmedArea)
                                    .Select(o => o.space)
                                    .FirstOrDefault();

                                if (winner == null)
                                {
                                    // fallback: rival with largest overlapArea
                                    winner = overlaps
                                        .OrderByDescending(o => o.overlapArea)
                                        .First().space;
                                }
                            }

                            // assign to the chosen winner
                            var id = PaintCell(cell, winner);
                            UpdateBudgets(winner, cell, overlaps, area);

                            cellAssignment[cell] = winner;
                            cellRegionMap[cell] = id;
                            assignedCells.Add(cell);
                            contested.Remove(cell);
                            regionIds.Add(id);

                            didAssign = true;
                            break;
                        }
                    }

                    if (didAssign) continue;

                    // 2b) highest-budget fallback
                    foreach (var sp in GlobalData.SavedSpaces.OrderByDescending(s => s.SquareTrimmedArea))
                    {
                        var myCells = contested
                            .Where(kv => !assignedCells.Contains(kv.Key) && kv.Value.Any(o => o.space == sp))
                            .ToList();
                        foreach (var kv in myCells)
                        {
                            var cell = kv.Key;
                            var overlaps = kv.Value;
                            if (overlaps.All(o => sp.SquareTrimmedArea >= o.space.SquareTrimmedArea))
                            {
                                double area = cell.Size * cell.Size;
                                var id = PaintCell(cell, sp);
                                UpdateBudgets(sp, cell, overlaps, area);
                                cellAssignment[cell] = sp;
                                cellRegionMap[cell] = id;
                                assignedCells.Add(cell);
                                contested.Remove(cell);
                                regionIds.Add(id);
                                didAssign = true;
                                break;
                            }
                        }
                        if (didAssign) break;
                    }
                    if (!didAssign) break;
                }




                // 3) Connectivity enforcement, skipping single-cell rooms
                bool changed;
                do
                {
                    changed = false;
                    foreach (var grp in cellAssignment.GroupBy(kv => kv.Value))
                    {
                        var room = grp.Key;
                        var myCells = grp.Select(kv => kv.Key).ToList();
                        if (myCells.Count <= 1)
                            continue;

                        var cellSet = new HashSet<ModuleGridCell>(myCells);
                        foreach (var cell in myCells)
                        {
                            bool hasNeighbor = cellSet.Any(n =>
                                (n.OriginX == cell.OriginX + cell.Size && n.OriginY == cell.OriginY) ||
                                (n.OriginX == cell.OriginX - cell.Size && n.OriginY == cell.OriginY) ||
                                (n.OriginX == cell.OriginX && n.OriginY == cell.OriginY + cell.Size) ||
                                (n.OriginX == cell.OriginX && n.OriginY == cell.OriginY - cell.Size));
                            if (!hasNeighbor)
                            {
                                // pick a new room excluding current
                                var rivals = originalOverlaps[cell]
                                    .Where(o => o.space != room)
                                    .OrderByDescending(o => o.overlapArea)
                                    .ToList();
                                if (!rivals.Any())
                                    continue;

                                var winner = rivals.First().space;
                                double area = cell.Size * cell.Size;
                                double overlapOld = originalOverlaps[cell].First(o => o.space == room).overlapArea;

                                // remove old and refund
                                _doc.Delete(cellRegionMap[cell]);
                                room.SquareTrimmedArea += (area - overlapOld);

                                // paint new
                                var id = PaintCell(cell, winner);
                                winner.SquareTrimmedArea -= (area - rivals.First().overlapArea);
                                cellAssignment[cell] = winner;
                                cellRegionMap[cell] = id;
                                regionIds.Add(id);
                                changed = true;
                            }
                        }
                    }
                } while (changed);

                tx.Commit();
            }
            return regionIds;
        }

        // Helpers to paint and update budgets
        private ElementId PaintCell(ModuleGridCell cell, SpaceNode room)
        {
            var ogs = new OverrideGraphicSettings()
                .SetSurfaceForegroundPatternColor(new Color(room.WpfColor.R, room.WpfColor.G, room.WpfColor.B))
                .SetSurfaceBackgroundPatternColor(new Color(room.WpfColor.R, room.WpfColor.G, room.WpfColor.B))
                .SetSurfaceForegroundPatternId(_fillPattern.Id)
                .SetSurfaceBackgroundPatternId(_fillPattern.Id)
                .SetSurfaceTransparency(0)
                .SetProjectionLineColor(new Color(room.WpfColor.R, room.WpfColor.G, room.WpfColor.B))
                .SetProjectionLineWeight(1);
            var region = FilledRegion.Create(_doc, _regionType.Id, _view.Id, new List<CurveLoop> { cell.Loop });
            _view.SetElementOverrides(region.Id, ogs);
            return region.Id;
        }

        private void UpdateBudgets(SpaceNode winner, ModuleGridCell cell, List<(SpaceNode space, double overlapArea)> overlaps, double cellArea)
        {
            var ov = overlaps.First(o => o.space == winner).overlapArea;
            winner.SquareTrimmedArea -= (cellArea - ov);
            foreach (var loser in overlaps.Where(o => o.space != winner))
                loser.space.SquareTrimmedArea += loser.overlapArea;
        }


















        ///////////////////////////////////////////////////////////////////////////PHASE 3 ///////////////////////////////////////////////////////////////////////////
        ///////////////////////////////////////////////////////////////////////////PHASE 3 ///////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////PHASE 3 ///////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////PHASE 3 ///////////////////////////////////////////////////////////////////////////







        public List<ElementId> Phase3ResolveEmptyCells(
        List<ModuleGridCell> cells,
        Dictionary<ModuleGridCell, SpaceNode> cellAssignments)
        {
            var regionIds = new List<ElementId>();
            // start with cells not yet in cellAssignments
            var emptyCells = new List<ModuleGridCell>(
                cells.Where(c => !cellAssignments.ContainsKey(c)));

            using (var tx = new Transaction(_doc, "Phase 3: Assign Empty Cells"))
            {
                tx.Start();

                while (emptyCells.Any())
                {
                    // 1) compute metrics for each empty cell
                    var best = emptyCells
                        .Select(cell =>
                        {
                            // find up/down/left/right neighbors
                            var neighbors = cells.Where(n =>
                                (n.OriginX == cell.OriginX &&
                                 Math.Abs(n.OriginY - cell.OriginY) == cell.Size)
                             || (n.OriginY == cell.OriginY &&
                                 Math.Abs(n.OriginX - cell.OriginX) == cell.Size))
                            .ToList();

                            // filter to those already assigned
                            var filledNbrs = neighbors
                                .Where(n => cellAssignments.ContainsKey(n))
                                .ToList();

                            // count how many distinct rooms surround
                            var rooms = new HashSet<SpaceNode>(
                                filledNbrs.Select(n => cellAssignments[n]));

                            return new
                            {
                                cell,
                                neighborCount = filledNbrs.Count,
                                uniqueRooms = rooms.Count,
                                surrounding = rooms
                            };
                        })
                        // pick by highest neighborCount, then lowest uniqueRooms
                        .OrderByDescending(x => x.neighborCount)
                        .ThenBy(x => x.uniqueRooms)
                        .First();

                    // 2) pick the adjacent room with the largest remaining budget
                    var targetRoom = best.surrounding
                        .OrderByDescending(r => r.SquareTrimmedArea)
                        .FirstOrDefault()
                        // if for some reason it has no surrounding rooms, fall back to global
                        ?? GlobalData.SavedSpaces
                             .OrderByDescending(sp => sp.SquareTrimmedArea)
                             .First();

                    // 3) fill the cell completely for that room
                    var reg = FilledRegion.Create(
                        _doc,
                        _regionType.Id,
                        _view.Id,
                        new List<CurveLoop> { best.cell.Loop });
                    _view.SetElementOverrides(
                        reg.Id,
                        MakeOGS(targetRoom).SetSurfaceTransparency(0));

                    regionIds.Add(reg.Id);

                    // 4) record & update
                    cellAssignments[best.cell] = targetRoom;
                    emptyCells.Remove(best.cell);
                    targetRoom.SquareTrimmedArea -= (best.cell.Size * best.cell.Size);
                }

                tx.Commit();
            }

            return regionIds;
        }











        /// <summary>
        /// PHASE 3: Fill every empty cell based on the Phase 2 drawing and assignments.
        /// We first “seed” our painted map from all existing FilledRegions (Phase 1 & 2),
        /// then repeatedly pick the best empty by neighbor‐scoring (including those seeded cells),
        /// falling back to largest‐budget only when truly isolated.
        /// </summary>
        public List<ElementId> Phase3ResolveBasedOnPhase2(List<ModuleGridCell> cells)
        {
            double tol = _doc.Application.ShortCurveTolerance;
            var view = _view;
            var newRegionIds = new List<ElementId>();

            // 1) fast lookup of cells by their lower‐left corner
            var cellLookup = cells.ToDictionary(c => (c.OriginX, c.OriginY));

            // 2) seed “painted” from all existing full-cell fills (Phase 1 & 2 results)
            //    mapping each cell → the SpaceNode that owns its current fill color
            var painted = new Dictionary<ModuleGridCell, SpaceNode>();
            foreach (var cell in cells)
            {
                // build the cell’s rect
                var cellRect = new UVRect(cell.OriginX, cell.OriginY, cell.Size, cell.Size);

                // find any FilledRegion that covers this entire cell
                var fr = new FilteredElementCollector(_doc, view.Id)
                    .OfClass(typeof(FilledRegion))
                    .Cast<FilledRegion>()
                    .FirstOrDefault(r => {
                        var bb = r.get_BoundingBox(view);
                        if (bb == null) return false;
                        var r2 = new UVRect(bb.Min.X, bb.Min.Y,
                                            bb.Max.X - bb.Min.X,
                                            bb.Max.Y - bb.Min.Y);
                        return r2.Intersect(cellRect).Area > tol;
                    });

                if (fr != null)
                {
                    // figure out which room that region belongs to by its line‐color
                    var col = view.GetElementOverrides(fr.Id).ProjectionLineColor;
                    var space = GlobalData.SavedSpaces.FirstOrDefault(sp =>
                        sp.WpfColor.R == col.Red &&
                        sp.WpfColor.G == col.Green &&
                        sp.WpfColor.B == col.Blue);
                    if (space != null)
                        painted[cell] = space;
                }
            }

            // 3) collect truly empty cells (those not already in 'painted')
            var empties = new HashSet<ModuleGridCell>(
                cells.Where(c => !painted.ContainsKey(c))
            );

            using (var tx = new Transaction(_doc, "Phase 3: Fill Empties (Based on Phase 2)"))
            {
                tx.Start();

                while (empties.Any())
                {
                    // 4) score each empty by how many distinct adjacent rooms it touches
                    var scored = empties
                        .Select(cell => {
                            var adj = new HashSet<SpaceNode>();
                            foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                            {
                                var key = (cell.OriginX + d.Item1 * cell.Size,
                                           cell.OriginY + d.Item2 * cell.Size);

                                // first check our in-memory painted cells
                                if (cellLookup.TryGetValue(key, out var nbr)
                                    && painted.TryGetValue(nbr, out var room))
                                {
                                    adj.Add(room);
                                    continue;
                                }

                                // fallback: check any existing FilledRegion geometry
                                var nbrRect = new UVRect(key.Item1, key.Item2, cell.Size, cell.Size);
                                var neighborFR = new FilteredElementCollector(_doc, view.Id)
                                    .OfClass(typeof(FilledRegion))
                                    .Cast<FilledRegion>()
                                    .FirstOrDefault(rg => {
                                        var bb = rg.get_BoundingBox(view);
                                        if (bb == null) return false;
                                        var r2 = new UVRect(bb.Min.X, bb.Min.Y,
                                                            bb.Max.X - bb.Min.X,
                                                            bb.Max.Y - bb.Min.Y);
                                        return r2.Intersect(nbrRect).Area > tol;
                                    });
                                if (neighborFR != null)
                                {
                                    var c2 = view.GetElementOverrides(neighborFR.Id).ProjectionLineColor;
                                    var room2 = GlobalData.SavedSpaces.FirstOrDefault(sp =>
                                        sp.WpfColor.R == c2.Red &&
                                        sp.WpfColor.G == c2.Green &&
                                        sp.WpfColor.B == c2.Blue);
                                    if (room2 != null) adj.Add(room2);
                                }
                            }

                            return new
                            {
                                cell,
                                neighborCount = adj.Count,
                                neighbors = adj
                            };
                        })
                        .OrderByDescending(x => x.neighborCount)
                        .ToList();

                    // 5) pick the top‐scoring empty (guaranteed non‐empty list)
                    var best = scored[0];

                    // 6) choose which room wins:
                    //    – if it has any touching neighbors, pick among them by largest budget
                    //    – otherwise pick the global largest‐budget room
                    SpaceNode winner;
                    if (best.neighbors.Any())
                    {
                        winner = best.neighbors
                            .OrderByDescending(r => r.SquareTrimmedArea)
                            .First();
                    }
                    else
                    {
                        winner = GlobalData.SavedSpaces
                            .OrderByDescending(sp => sp.SquareTrimmedArea)
                            .First();
                    }

                    // 7) paint the cell
                    var region = FilledRegion.Create(
                        _doc, _regionType.Id, view.Id,
                        new List<CurveLoop> { best.cell.Loop }
                    );
                    var id = region.Id;
                    _view.SetElementOverrides(id, MakeOGS(winner).SetSurfaceTransparency(0));
                    newRegionIds.Add(id);

                    // 8) deduct its full area from the winner’s budget
                    winner.SquareTrimmedArea -= best.cell.Size * best.cell.Size;

                    // 9) record it as painted and remove from empties
                    painted[best.cell] = winner;
                    empties.Remove(best.cell);
                }

                tx.Commit();
            }

            return newRegionIds;
        }













































        public List<ElementId> FillSingleRoomPartialCells(List<ModuleGridCell> cells)
        {
            var regionIds = new List<ElementId>();
            // 1) Find all cells with exactly one overlapping space and partial overlap
            var partials = new List<(ModuleGridCell cell, SpaceNode room, double overlap)>();
            foreach (var cell in cells)
            {
                SpaceNode single = null;
                double overlap = 0;
                int count = 0;
                double cellArea = cell.Size * cell.Size;

                foreach (var sp in GlobalData.SavedSpaces)
                {
                    // compute axis-aligned overlap
                    double sr = Math.Sqrt(sp.Area / Math.PI);
                    double sx0 = sp.Position.X - sr, sy0 = sp.Position.Y - sr;
                    double sx1 = sx0 + 2 * sr, sy1 = sy0 + 2 * sr;
                    double ix = Math.Max(0,
                                 Math.Min(cell.OriginX + cell.Size, sx1)
                               - Math.Max(cell.OriginX, sx0));
                    double iy = Math.Max(0,
                                 Math.Min(cell.OriginY + cell.Size, sy1)
                               - Math.Max(cell.OriginY, sy0));
                    if (ix > 0 && iy > 0)
                    {
                        count++;
                        if (count == 1)
                        {
                            single = sp;
                            overlap = ix * iy;
                        }
                        else break;
                    }
                }

                // exactly one room AND partial overlap
                if (count == 1 && overlap > 0 && overlap < cellArea)
                    partials.Add((cell, single, overlap));
            }

            // 2) Fill each partial cell completely
            using (var tx = new Transaction(_doc, "Phase 1b: Fill Partial Single-Overlap"))
            {
                tx.Start();
                foreach (var (cell, room, _) in partials)
                {
                    var fullReg = FilledRegion.Create(
                        _doc, _regionType.Id, _view.Id,
                        new List<CurveLoop> { cell.Loop });
                    // zero transparency = solid
                    _view.SetElementOverrides(
                        fullReg.Id,
                        MakeOGS(room).SetSurfaceTransparency(0));
                    regionIds.Add(fullReg.Id);

                }
                tx.Commit();
            }

            return regionIds;
        }



        public List<ElementId> ClearSingleRoomPartialCells(List<ModuleGridCell> cells)
        {
            var deletedIds = new List<ElementId>();

            // 1) Find all cells with exactly one overlapping space and partial overlap
            var partials = new List<ModuleGridCell>();
            foreach (var cell in cells)
            {
                int count = 0;
                double overlap = 0, cellArea = cell.Size * cell.Size;

                foreach (var sp in GlobalData.SavedSpaces)
                {
                    double r = Math.Sqrt(sp.Area / Math.PI);
                    double sx0 = sp.Position.X - r, sy0 = sp.Position.Y - r;
                    double sx1 = sx0 + 2 * r, sy1 = sy0 + 2 * r;

                    double ix = Math.Max(0,
                                Math.Min(cell.OriginX + cell.Size, sx1)
                              - Math.Max(cell.OriginX, sx0));
                    double iy = Math.Max(0,
                                Math.Min(cell.OriginY + cell.Size, sy1)
                              - Math.Max(cell.OriginY, sy0));

                    if (ix > 0 && iy > 0)
                    {
                        count++;
                        overlap = (count == 1) ? ix * iy : overlap;
                        if (count > 1) break;
                    }
                }

                if (count == 1 && overlap > 0 && overlap < cellArea)
                    partials.Add(cell);
            }

            // 2) Delete all regions assigned to each partial cell
            using (var tx = new Transaction(_doc, "Phase 1b: Clear Partial Single-Overlap"))
            {
                tx.Start();
                foreach (var cell in partials)
                {
                    // assume ModuleGridCell.RegionIds is your List<ElementId> of fills in that cell
                    foreach (var regId in cell.RegionIds)
                    {
                        deletedIds.Add(regId);
                        _doc.Delete(regId);
                    }
                    cell.RegionIds.Clear();
                }
                tx.Commit();
            }

            return deletedIds;
        }









        /// <summary>
        /// Re-snaps trimmed loops into their respective ModuleGridCell, clipping each to the cell bounds,
        /// deletes old regions, and creates new one-per-cell loops with solid overrides.
        /// </summary>
        /// <summary>
        /// Re-snaps trimmed loops into their respective ModuleGridCell, clipping each to the cell bounds,
        /// deletes old regions, and creates new one-per-cell loops with solid overrides.
        /// </summary>
        public List<ElementId> ResnapTrimmedLoopsIntoCells(
            IList<ModuleGridCell> moduleCells,
            IEnumerable<GridTrimmer.TrimResult> trims,
            FillPatternElement fillPatternOverride)
        {
            var newRegionIds = new List<ElementId>();
            double tol = _doc.Application.ShortCurveTolerance;

            // map each cell -> (space -> loops)
            var loopsByCellSpace = moduleCells
                .ToDictionary(
                    c => c,
                    c => new Dictionary<SpaceNode, List<CurveLoop>>()
                );

            using (var tx = new Transaction(_doc, "Re-snap trimmed loops into cells"))
            {
                tx.Start();

                // 1) Bucket & delete old regions
                foreach (var trim in trims.Where(t => t.RegionId.IntegerValue > 0))
                {
                    var pts = trim.Loop
                        .Cast<Curve>()
                        .SelectMany(c => new[] { c.GetEndPoint(0), c.GetEndPoint(1) })
                        .ToList();
                    double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
                    double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                    var loopRect = new UVRect(minX, minY, maxX - minX, maxY - minY);

                    foreach (var cell in moduleCells)
                    {
                        var cellRect = new UVRect(cell.OriginX, cell.OriginY, cell.Size, cell.Size);
                        var piece = loopRect.Intersect(cellRect);
                        if (piece.W <= tol || piece.H <= tol) continue;
                        var pieceLoop = piece.ToCurveLoop();

                        var dict = loopsByCellSpace[cell];
                        if (!dict.ContainsKey(trim.Space))
                            dict[trim.Space] = new List<CurveLoop>();
                        dict[trim.Space].Add(pieceLoop);
                    }

                    _doc.Delete(trim.RegionId);
                }

                // 2) Re-create a new region per (cell x space)
                foreach (var kv in loopsByCellSpace)
                {
                    var cell = kv.Key;
                    foreach (var spaceEntry in kv.Value)
                    {
                        var space = spaceEntry.Key;
                        var loops = spaceEntry.Value;
                        if (loops.Count == 0) continue;

                        var ogs = new OverrideGraphicSettings()
                            .SetSurfaceForegroundPatternColor(new Color(space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                            .SetSurfaceBackgroundPatternColor(new Color(space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                            .SetSurfaceForegroundPatternId(fillPatternOverride.Id)
                            .SetSurfaceBackgroundPatternId(fillPatternOverride.Id)
                            .SetSurfaceTransparency(0)
                            .SetProjectionLineColor(new Color(space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                            .SetProjectionLineWeight(1);

                        foreach (var loop in loops)
                        {
                            var region = FilledRegion.Create(
                                _doc,
                                _regionType.Id,
                                _view.Id,
                                new[] { loop }
                            );
                            var id = region.Id;
                            _view.SetElementOverrides(id, ogs);
                            newRegionIds.Add(id);
                            // record on cell for later clearing
                            cell.RegionIds.Add(id);
                        }
                    }
                }

                tx.Commit();
            }

            return newRegionIds;
        }




        /// <summary>
        /// Shows each room's remaining trimmed area in a TaskDialog.
        /// </summary>
        public void ShowTrimmedAreas()
        {
            // Build the message text
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Room Remaining Trimmed Areas:");
            foreach (var sp in GlobalData.SavedSpaces)
            {
                sb.AppendLine($" - {sp.Name}: {sp.SquareTrimmedArea:F2}");
            }

            // Display in Revit TaskDialog
            TaskDialog.Show(
                "Trimmed Areas",
                sb.ToString()
            );
        }
















    }


}
