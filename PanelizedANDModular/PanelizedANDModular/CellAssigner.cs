using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using PanelizedAndModularFinal;
using static PanelizedAndModularFinal.CellAssigner;

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
        /// PHASE 0: resolve every two-room overlap as a single patch,
        /// winner takes the *entire* overlap region, never partially by cell,
        /// and each pair of rooms is processed exactly once.
        /// Tiny overlaps are "inflated" to avoid Revit curve tolerance errors
        /// by centering a minimal-size expansion and clamping to the cell boundary.
        /// Any remaining invalid patches are skipped without crashing.
        /// </summary>
        public List<ElementId> Phase0ResolveMultiOverlaps(
            IList<ModuleGridCell> cells,
            IEnumerable<GridTrimmer.TrimResult> trims,
            FillPatternElement fillPatternOverride)
        {
            var allRegionIds = new List<ElementId>();
            allRegionIds.AddRange(
                ResnapTrimmedLoopsIntoCells(cells, trims, fillPatternOverride));

            double tol = _doc.Application.ShortCurveTolerance;
            var unresolved = new List<SpaceNode>(GlobalData.SavedSpaces);
            var processedPairs = new HashSet<(SpaceNode, SpaceNode)>();

            using (var tx = new Transaction(_doc, "Phase 0: Multi-Room Overlap"))
            {
                tx.Start();

                while (unresolved.Any())
                {
                    // 1) pick the room with the largest current budget
                    unresolved = unresolved
                        .OrderByDescending(sp => sp.SquareTrimmedArea)
                        .ToList();
                    var winner = unresolved.First();
                    unresolved.RemoveAt(0);

                    // 2) compare it to every other still-unresolved room
                    foreach (var loser in unresolved)
                    {
                        if (processedPairs.Contains((winner, loser)) ||
                            processedPairs.Contains((loser, winner)))
                            continue;

                        // 3) gather all overlap patches inside each cell
                        var cellOverlaps = new List<(ModuleGridCell cell, UVRect patch)>();
                        foreach (var cell in cells)
                        {
                            var wOpt = ComputeIntersection(cell, winner);
                            var lOpt = ComputeIntersection(cell, loser);
                            if (!wOpt.HasValue || !lOpt.HasValue) continue;

                            var rawPatch = wOpt.Value.Intersect(lOpt.Value);
                            if (rawPatch.Area <= 0) continue;
                            cellOverlaps.Add((cell, rawPatch));
                        }
                        if (!cellOverlaps.Any()) continue;

                        // 4) decide who really wins this overlap
                        SpaceNode owner, other;
                        if (winner.SquareTrimmedArea < loser.SquareTrimmedArea)
                        {
                            owner = loser;
                            other = winner;
                        }
                        else
                        {
                            owner = winner;
                            other = loser;
                        }

                        // 5) paint each cell-patch for the owner, inflating tiny slivers
                        double totalLostArea = 0;
                        foreach (var (cell, patch) in cellOverlaps)
                        {
                            // center-inflate to at least tol in each tiny dimension
                            double w = patch.W < tol ? tol : patch.W;
                            double h = patch.H < tol ? tol : patch.H;
                            double dx = (w - patch.W) / 2.0;
                            double dy = (h - patch.H) / 2.0;
                            var px = patch.X - dx;
                            var py = patch.Y - dy;
                            // clamp to cell boundary
                            px = Math.Max(cell.OriginX, px);
                            py = Math.Max(cell.OriginY, py);
                            w = Math.Min(w, cell.OriginX + cell.Size - px);
                            h = Math.Min(h, cell.OriginY + cell.Size - py);
                            var safePatch = new UVRect(px, py, w, h);

                            try
                            {
                                var reg = FilledRegion.Create(
                                    _doc, _regionType.Id, _view.Id,
                                    new[] { safePatch.ToCurveLoop() });
                                _view.SetElementOverrides(
                                    reg.Id, MakeOGS(owner).SetSurfaceTransparency(0));
                                allRegionIds.Add(reg.Id);
                                cell.RegionIds.Add(reg.Id);
                            }
                            catch (ArgumentException)
                            {
                                Debug.Print($"Skipped tiny patch at cell ({cell.OriginX},{cell.OriginY}): too small.");
                            }

                            totalLostArea += patch.Area;
                        }

                        // 6) carve the loser’s remainder in those same cells, also inflating micro-strips
                        foreach (var (cell, patch) in cellOverlaps)
                        {
                            var otherOpt = ComputeIntersection(cell, other);
                            if (!otherOpt.HasValue) continue;

                            foreach (var strip in SubtractRectangles(otherOpt.Value, patch))
                            {
                                // center-inflate small strips similarly
                                double w = strip.W < tol ? tol : strip.W;
                                double h = strip.H < tol ? tol : strip.H;
                                double dx = (w - strip.W) / 2.0;
                                double dy = (h - strip.H) / 2.0;
                                var px = strip.X - dx;
                                var py = strip.Y - dy;
                                px = Math.Max(cell.OriginX, px);
                                py = Math.Max(cell.OriginY, py);
                                w = Math.Min(w, cell.OriginX + cell.Size - px);
                                h = Math.Min(h, cell.OriginY + cell.Size - py);
                                var safeStrip = new UVRect(px, py, w, h);

                                try
                                {
                                    var reg = FilledRegion.Create(
                                        _doc, _regionType.Id, _view.Id,
                                        new[] { safeStrip.ToCurveLoop() });
                                    _view.SetElementOverrides(
                                        reg.Id, MakeOGS(other).SetSurfaceTransparency(0));
                                    allRegionIds.Add(reg.Id);
                                    cell.RegionIds.Add(reg.Id);
                                }
                                catch (ArgumentException)
                                {
                                    Debug.Print($"Skipped tiny carve at cell ({cell.OriginX},{cell.OriginY}).");
                                }
                            }
                        }

                        // 7) winners keep their budget; losers always gain
                        other.SquareTrimmedArea += totalLostArea;

                        // 8) mark this pair done so we never revisit it
                        processedPairs.Add((winner, loser));
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




        public List<ElementId> Phase1ResolveSingleOverlap(List<ModuleGridCell> cells)
        {
            var deletedOrAddedIds = new List<ElementId>();
            double tol = _doc.Application.ShortCurveTolerance;

            // 1) process rooms by descending remaining trimmed area
            var rooms = GlobalData.SavedSpaces
                .OrderByDescending(sp => sp.SquareTrimmedArea)
                .ToList();

            using (var tx = new Transaction(_doc, "Phase 1: Fill & Clear Single-Overlap Cells"))
            {
                tx.Start();

                foreach (var space in rooms)
                {
                   

                    // 2) identify partial cells
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

                    // 3) sort by descending overlap
                    var toProcess = partials.OrderByDescending(p => p.overlap).ToList();
                    var skipped = new List<(ModuleGridCell cell, double overlap)>();
                    var filled = new HashSet<ModuleGridCell>();

                    // 4) first pass: fill best overlaps
                    foreach (var (cell, overlap) in toProcess)
                    {
                        double cellArea = cell.Size * cell.Size;
                        double fillAmount = cellArea - overlap;

                        if (space.SquareTrimmedArea > 0 &&
                            (filled.Count == 0 || IsAdjacent(cell, filled)))
                        {
                            

                            var reg = FilledRegion.Create(
                                _doc, _regionType.Id, _view.Id,
                                new List<CurveLoop> { cell.Loop });
                            _view.SetElementOverrides(
                                reg.Id, MakeOGS(space).SetSurfaceTransparency(0));
                            cell.RegionIds.Add(reg.Id);
                            deletedOrAddedIds.Add(reg.Id);

                            filled.Add(cell);
                            space.SquareTrimmedArea -= fillAmount;
                        }
                        else
                        {
                            skipped.Add((cell, overlap));
                        }
                    }

                    // 5) adjacency passes
                    bool didFill;
                    do
                    {
                        didFill = false;
                        foreach (var entry in skipped.ToList())
                        {
                            var cell = entry.cell;
                            var overlap = entry.overlap;
                            double cellArea = cell.Size * cell.Size;
                            double fillAmount = cellArea - overlap;

                            if (space.SquareTrimmedArea > 0 && IsAdjacent(cell, filled))
                            {
                               

                                var reg = FilledRegion.Create(
                                    _doc, _regionType.Id, _view.Id,
                                    new List<CurveLoop> { cell.Loop });
                                _view.SetElementOverrides(reg.Id, MakeOGS(space).SetSurfaceTransparency(0));
                                cell.RegionIds.Add(reg.Id);
                                deletedOrAddedIds.Add(reg.Id);

                                filled.Add(cell);
                                space.SquareTrimmedArea -= fillAmount;
                                skipped.Remove(entry);
                                didFill = true;
                                break;
                            }
                        }
                    } while (didFill);

                    // 6) final pass: fill or clear leftovers
                    foreach (var entry in skipped.OrderByDescending(p => p.overlap))
                    {
                        var cell = entry.cell;
                        var overlap = entry.overlap;
                        double cellArea = cell.Size * cell.Size;
                        double fillAmount = cellArea - overlap;

                        if (space.SquareTrimmedArea > 0)
                        {
                            

                            var reg = FilledRegion.Create(
                                _doc, _regionType.Id, _view.Id,
                                new List<CurveLoop> { cell.Loop });
                            _view.SetElementOverrides(reg.Id, MakeOGS(space).SetSurfaceTransparency(0));
                            cell.RegionIds.Add(reg.Id);
                            deletedOrAddedIds.Add(reg.Id);

                            space.SquareTrimmedArea -= fillAmount;
                        }
                        else
                        {
                            

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
















        ///////////////////////////////////////////////////////////////////////////PHASE 2 ///////////////////////////////////////////////////////////////////////////
        ///////////////////////////////////////////////////////////////////////////PHASE 2 ///////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////PHASE 2 ///////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////PHASE 2 ///////////////////////////////////////////////////////////////////////////





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

        private void UpdateBudgets(
            SpaceNode winner,
            ModuleGridCell cell,
            List<(SpaceNode space, double overlapArea)> overlaps,
            double cellArea)
        {
            var ov = overlaps.First(o => o.space == winner).overlapArea;
            winner.SquareTrimmedArea -= (cellArea - ov);
            foreach (var loser in overlaps.Where(o => o.space != winner))
                loser.space.SquareTrimmedArea += loser.overlapArea;
        }

        private SpaceNode ChooseWinnerPhase2Rules(
            ModuleGridCell cell,
            List<(SpaceNode space, double overlapArea)> overlaps)
        {
            double area = cell.Size * cell.Size;
            var top = overlaps.FirstOrDefault(o => o.overlapArea > 0.5 * area);
            if (top.space != null)
            {
                var winner = top.space;
                if (winner.SquareTrimmedArea <= 0)
                {
                    var byBudget = overlaps
                        .Where(o => o.space.SquareTrimmedArea > 0)
                        .OrderByDescending(o => o.space.SquareTrimmedArea)
                        .FirstOrDefault();
                    winner = byBudget.space ?? overlaps.OrderByDescending(o => o.overlapArea).First().space;
                }
                return winner;
            }
            foreach (var sp in GlobalData.SavedSpaces.OrderByDescending(s => s.SquareTrimmedArea))
            {
                if (overlaps.Any(o => o.space == sp) && overlaps.All(o => sp.SquareTrimmedArea >= o.space.SquareTrimmedArea))
                    return sp;
            }
            return overlaps.OrderByDescending(o => o.overlapArea).First().space;
        }

        private bool IsAdjacent(ModuleGridCell a, ModuleGridCell b)
        {
            return (a.OriginX == b.OriginX + b.Size && a.OriginY == b.OriginY) ||
                   (a.OriginX == b.OriginX - b.Size && a.OriginY == b.OriginY) ||
                   (a.OriginX == b.OriginX && a.OriginY == b.OriginY + b.Size) ||
                   (a.OriginX == b.OriginX && a.OriginY == b.OriginY - b.Size);
        }

        public List<ElementId> Phase2ResolveContestedCells(List<ModuleGridCell> cells)
        {
            var regionIds = new List<ElementId>();
            var cellAssignment = new Dictionary<ModuleGridCell, SpaceNode>();
            var cellRegionMap = new Dictionary<ModuleGridCell, ElementId>();

            // 1) Build contested map inline
            var contested = new Dictionary<ModuleGridCell, List<(SpaceNode space, double overlapArea)>>();
            var tol = _doc.Application.ShortCurveTolerance;
            foreach (var cell in cells)
            {
                double cellArea = cell.Size * cell.Size;
                var paintedArea = new Dictionary<SpaceNode, double>();
                foreach (var rid in cell.RegionIds)
                {
                    var sp = GetSpaceForRegionId(rid);
                    if (sp == null) continue;
                    var fr = _doc.GetElement(rid) as FilledRegion;
                    if (fr == null) continue;
                    var bb = fr.get_BoundingBox(_view);
                    if (bb == null) continue;
                    double w = bb.Max.X - bb.Min.X, h = bb.Max.Y - bb.Min.Y;
                    paintedArea[sp] = paintedArea.GetValueOrDefault(sp) + (w * h);
                }
                var fullyCovered = new HashSet<SpaceNode>();
                foreach (var kv in paintedArea)
                {
                    double pa = kv.Value;
                    double others = paintedArea.Where(x => x.Key != kv.Key).Sum(x => x.Value);
                    if (pa > 0 && others >= pa - tol)
                        fullyCovered.Add(kv.Key);
                }
                var overlaps = new List<(SpaceNode space, double overlapArea)>();
                foreach (var sp in GlobalData.SavedSpaces)
                {
                    if (fullyCovered.Contains(sp)) continue;
                    var intrOpt = ComputeIntersection(cell, sp);
                    if (!intrOpt.HasValue) continue;
                    double areaOverlap = intrOpt.Value.Area;
                    if (areaOverlap > 0)
                        overlaps.Add((sp, areaOverlap));
                }
                if (overlaps.Count >= 2)
                    contested[cell] = overlaps;
            }
            var originalOverlaps = contested.ToDictionary(
                kv => kv.Key,
                kv => new List<(SpaceNode, double)>(kv.Value)
            );
            var roomContested = GlobalData.SavedSpaces.ToDictionary(
                sp => sp,
                sp => new HashSet<ModuleGridCell>());
            foreach (var kv in contested)
                foreach (var (sp, _) in kv.Value)
                    roomContested[sp].Add(kv.Key);

            // all model edits inside this transaction
            using (var tx = new Transaction(_doc, "Phase 2: Resolve Contested Cells"))
            {
                tx.Start();

                // 2) Resolve by room-priority
                while (contested.Count > 0)
                {
                    var room = GlobalData.SavedSpaces
                        .Where(r => roomContested[r].Count > 0)
                        .OrderByDescending(r => r.SquareTrimmedArea)
                        .FirstOrDefault();
                    if (room == null) break;

                    var assigned = cellAssignment.Where(kv => kv.Value == room).Select(kv => kv.Key).ToHashSet();
                    var toResolve = roomContested[room]
                        .OrderBy(c => assigned.Any(a => IsAdjacent(a, c)) ? 0 : 1)
                        .ToList();

                    foreach (var cell in toResolve)
                    {
                        var overlapsList = contested[cell];
                        SpaceNode winner = overlapsList.Select(o => o.space)
                            .FirstOrDefault(sp => assigned.Any(a => IsAdjacent(a, cell)));
                        winner ??= ChooseWinnerPhase2Rules(cell, overlapsList);
                        if (winner == null) continue;

                        double area = cell.Size * cell.Size;
                        var id = PaintCell(cell, winner);
                        UpdateBudgets(winner, cell, overlapsList, area);
                        foreach (var o in overlapsList.Where(o => o.space != winner))
                            o.space.SquareTrimmedArea += o.overlapArea;

                        cellAssignment[cell] = winner;
                        cellRegionMap[cell] = id;
                        regionIds.Add(id);
                        contested.Remove(cell);
                        foreach (var sp in overlapsList.Select(o => o.space))
                            roomContested[sp].Remove(cell);
                    }
                }

                // 3) Connectivity enforcement
                bool changed;
                do
                {
                    changed = false;
                    foreach (var grp in cellAssignment.GroupBy(kv => kv.Value))
                    {
                        var r = grp.Key;
                        var myCells = grp.Select(kv => kv.Key).ToList();
                        if (myCells.Count <= 1) continue;
                        var cellSet = new HashSet<ModuleGridCell>(myCells);
                        foreach (var cell in myCells)
                        {
                            if (cellSet.Any(n => IsAdjacent(n, cell))) continue;
                            ReassignOrphan(cell, r, originalOverlaps, cellAssignment, cellRegionMap, regionIds);
                            changed = true;
                            break;
                        }
                        if (changed) break;
                    }
                } while (changed);

                // 4) Final orphan pass: fix any lone cells
                bool orphanFound;
                do
                {
                    orphanFound = false;
                    foreach (var kv in cellAssignment.ToList())
                    {
                        var cell = kv.Key;
                        var rm = kv.Value;
                        bool hasNbr = cellAssignment.Keys
                            .Any(n => IsAdjacent(n, cell) && cellAssignment[n] == rm);
                        if (hasNbr) continue;

                        var rivals = originalOverlaps[cell]
                            .Where(o => o.Item1 != rm
                                     && cellAssignment.Keys.Any(n => cellAssignment[n] == o.Item1 && IsAdjacent(n, cell)))
                            .ToList();
                        if (!rivals.Any()) continue;

                        if (cellRegionMap.TryGetValue(cell, out var oldId) && _doc.GetElement(oldId) != null)
                            _doc.Delete(oldId);

                        double fullArea = cell.Size * cell.Size;
                        double oldOv = originalOverlaps[cell].First(o => o.Item1 == rm).Item2;
                        rm.SquareTrimmedArea += (fullArea - oldOv);

                        var newWin = ChooseWinnerPhase2Rules(cell, rivals.Select(o => (space: o.Item1, overlapArea: o.Item2)).ToList());
                        if (newWin != null)
                        {
                            var newId = PaintCell(cell, newWin);
                            UpdateBudgets(newWin, cell, rivals.Select(o => (space: o.Item1, overlapArea: o.Item2)).ToList(), fullArea);
                            foreach (var o in rivals.Where(o => o.Item1 != newWin))
                                o.Item1.SquareTrimmedArea += o.Item2;
                            cellAssignment[cell] = newWin;
                            cellRegionMap[cell] = newId;
                            regionIds.Add(newId);
                        }

                        orphanFound = true;
                        break;
                    }
                } while (orphanFound);

                // commit only after all edits
                tx.Commit();
            }

            return regionIds;
        }

        private void ReassignOrphan(
            ModuleGridCell cell,
            SpaceNode oldRoom,
            Dictionary<ModuleGridCell, List<(SpaceNode space, double overlapArea)>> originalOverlaps,
            Dictionary<ModuleGridCell, SpaceNode> cellAssignment,
            Dictionary<ModuleGridCell, ElementId> cellRegionMap,
            List<ElementId> regionIds)
        {
            if (cellRegionMap.TryGetValue(cell, out var oldId) && _doc.GetElement(oldId) != null)
                _doc.Delete(oldId);
            double fullArea = cell.Size * cell.Size;
            double oldOv = originalOverlaps[cell].First(o => o.space == oldRoom).overlapArea;
            oldRoom.SquareTrimmedArea += (fullArea - oldOv);
            var rivals = originalOverlaps[cell]
                .Where(o => o.space != oldRoom &&
                    cellAssignment.Keys.Any(n => cellAssignment[n] == o.space && IsAdjacent(n, cell)))
                .ToList();
            if (!rivals.Any()) return;
            var newWin = ChooseWinnerPhase2Rules(cell, rivals);
            if (newWin != null)
            {
                var newId = PaintCell(cell, newWin);
                UpdateBudgets(newWin, cell, rivals, fullArea);
                foreach (var o in rivals.Where(o => o.space != newWin))
                    o.space.SquareTrimmedArea += o.overlapArea;
                cellAssignment[cell] = newWin;
                cellRegionMap[cell] = newId;
                regionIds.Add(newId);
            }
        }

        private SpaceNode GetSpaceForRegionId(ElementId rid)
        {
            // implement a lookup of region ID to SpaceNode here
            return GlobalData.SavedSpaces.FirstOrDefault();
        }






        ///////////////////////////////////////////////////////////////////////////PHASE 3 ///////////////////////////////////////////////////////////////////////////
        ///////////////////////////////////////////////////////////////////////////PHASE 3 ///////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////PHASE 3 ///////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////PHASE 3 ///////////////////////////////////////////////////////////////////////////







        //adjacency weird here
        //public List<ElementId> Phase3ResolveBasedOnPhase2(List<ModuleGridCell> cells)
        //{
        //    double tol = _doc.Application.ShortCurveTolerance;
        //    var view = _view;
        //    var newRegionIds = new List<ElementId>();
        //    var cellLookup = cells.ToDictionary(c => (c.OriginX, c.OriginY));

        //    // 1) Seed painted map exactly as before
        //    var painted = new Dictionary<ModuleGridCell, SpaceNode>();
        //    foreach (var cell in cells)
        //    {
        //        var rect = new UVRect(cell.OriginX, cell.OriginY, cell.Size, cell.Size);
        //        var fr = new FilteredElementCollector(_doc, view.Id)
        //            .OfClass(typeof(FilledRegion))
        //            .Cast<FilledRegion>()
        //            .FirstOrDefault(r => {
        //                var bb = r.get_BoundingBox(view);
        //                if (bb == null) return false;
        //                var r2 = new UVRect(bb.Min.X, bb.Min.Y, bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y);
        //                return r2.Intersect(rect).Area > tol;
        //            });
        //        if (fr == null) continue;
        //        var col = view.GetElementOverrides(fr.Id).ProjectionLineColor;
        //        var space = GlobalData.SavedSpaces
        //            .FirstOrDefault(sp => sp.WpfColor.R == col.Red
        //                               && sp.WpfColor.G == col.Green
        //                               && sp.WpfColor.B == col.Blue);
        //        if (space != null) painted[cell] = space;
        //    }

        //    var empties = new HashSet<ModuleGridCell>(cells.Where(c => !painted.ContainsKey(c)));
        //    var deltas = new[] { (dx: 1, dy: 0), (-1, 0), (0, 1), (0, -1) };

        //    using (var tx = new Transaction(_doc, "Phase 3: Fill Empties"))
        //    {
        //        tx.Start();
        //        while (empties.Any())
        //        {
        //            // 2) compute metrics for every empty
        //            var scored = empties
        //                .Select(cell => {
        //                    int neighborCount = 0;
        //                    var rooms = new HashSet<SpaceNode>();

        //                    foreach (var (dx, dy) in deltas)
        //                    {
        //                        var key = (cell.OriginX + dx * cell.Size,
        //                                   cell.OriginY + dy * cell.Size);
        //                        if (!cellLookup.TryGetValue(key, out var nbr))
        //                            continue;

        //                        // did nbr have *any* fill? if so, count it
        //                        var nbrRect = new UVRect(key.Item1, key.Item2, cell.Size, cell.Size);
        //                        var hits = new FilteredElementCollector(_doc, view.Id)
        //                            .OfClass(typeof(FilledRegion))
        //                            .Cast<FilledRegion>()
        //                            .Where(fr => {
        //                                var bb = fr.get_BoundingBox(view);
        //                                if (bb == null) return false;
        //                                var r2 = new UVRect(bb.Min.X, bb.Min.Y,
        //                                                    bb.Max.X - bb.Min.X,
        //                                                    bb.Max.Y - bb.Min.Y);
        //                                return r2.Intersect(nbrRect).Area > tol;
        //                            })
        //                            .OrderByDescending(fr => fr.Id.IntegerValue)
        //                            .ToList();

        //                        if (hits.Any())
        //                        {
        //                            neighborCount++;
        //                            var top = hits[0];
        //                            var col = view.GetElementOverrides(top.Id).ProjectionLineColor;
        //                            var room = GlobalData.SavedSpaces
        //                                .FirstOrDefault(sp => sp.WpfColor.R == col.Red
        //                                                   && sp.WpfColor.G == col.Green
        //                                                   && sp.WpfColor.B == col.Blue);
        //                            if (room != null) rooms.Add(room);
        //                        }
        //                    }

        //                    return new
        //                    {
        //                        cell,
        //                        neighborCount,
        //                        distinctCount = rooms.Count,
        //                        rooms
        //                    };
        //                })
        //                // 3) highest neighborCount, then lowest distinctCount
        //                .OrderByDescending(x => x.neighborCount)
        //                .ThenBy(x => x.distinctCount)
        //                .ToList();

        //            var best = scored.First();

        //            // 4) pick winner among its adjacent rooms by largest budget
        //            SpaceNode winner = best.rooms
        //                .OrderByDescending(r => r.SquareTrimmedArea)
        //                .FirstOrDefault()
        //                // fallback if somehow no neighbors
        //                ?? GlobalData.SavedSpaces.OrderByDescending(sp => sp.SquareTrimmedArea).First();

        //            // 5) paint + deduct
        //            var region = FilledRegion.Create(
        //                _doc, _regionType.Id, view.Id,
        //                new List<CurveLoop> { best.cell.Loop }
        //            );
        //            _view.SetElementOverrides(region.Id,
        //                MakeOGS(winner).SetSurfaceTransparency(0));
        //            newRegionIds.Add(region.Id);

        //            winner.SquareTrimmedArea -= best.cell.Size * best.cell.Size;
        //            painted[best.cell] = winner;
        //            empties.Remove(best.cell);
        //        }
        //        tx.Commit();
        //    }

        //    return newRegionIds;
        //}





        public List<ElementId> Phase3ResolveBasedOnPhase2(List<ModuleGridCell> cells)
        {
            double tol = _doc.Application.ShortCurveTolerance;
            var view = _view;
            var newRegionIds = new List<ElementId>();
            var cellLookup = cells.ToDictionary(c => (c.OriginX, c.OriginY));

            // 1) Seed painted map
            var painted = new Dictionary<ModuleGridCell, SpaceNode>();
            foreach (var cell in cells)
            {
                var rect = new UVRect(cell.OriginX, cell.OriginY, cell.Size, cell.Size);
                var fr = new FilteredElementCollector(_doc, view.Id)
                    .OfClass(typeof(FilledRegion))
                    .Cast<FilledRegion>()
                    .FirstOrDefault(r => {
                        var bb = r.get_BoundingBox(view);
                        if (bb == null) return false;
                        var r2 = new UVRect(bb.Min.X, bb.Min.Y,
                                            bb.Max.X - bb.Min.X,
                                            bb.Max.Y - bb.Min.Y);
                        return r2.Intersect(rect).Area > tol;
                    });
                if (fr == null) continue;

                var col = view.GetElementOverrides(fr.Id).ProjectionLineColor;
                var space = GlobalData.SavedSpaces
                    .FirstOrDefault(sp => sp.WpfColor.R == col.Red
                                       && sp.WpfColor.G == col.Green
                                       && sp.WpfColor.B == col.Blue);
                if (space != null) painted[cell] = space;
            }

            var empties = new HashSet<ModuleGridCell>(cells.Where(c => !painted.ContainsKey(c)));
            var deltas = new[] { (dx: 1, dy: 0), (-1, 0), (0, 1), (0, -1) };

            using (var tx = new Transaction(_doc, "Phase 3: Fill Empties"))
            {
                tx.Start();

                // Pass 1: adjacency‐only
                while (true)
                {
                    var scored = empties.Select(cell => {
                        int neighborCount = 0;
                        var rooms = new HashSet<SpaceNode>();

                        foreach (var (dx, dy) in deltas)
                        {
                            var key = (cell.OriginX + dx * cell.Size,
                                       cell.OriginY + dy * cell.Size);
                            if (!cellLookup.TryGetValue(key, out var nbr)) continue;

                            var nbrRect = new UVRect(key.Item1, key.Item2, cell.Size, cell.Size);
                            var hits = new FilteredElementCollector(_doc, view.Id)
                                .OfClass(typeof(FilledRegion))
                                .Cast<FilledRegion>()
                                .Where(fr => {
                                    var bb = fr.get_BoundingBox(view);
                                    if (bb == null) return false;
                                    var r2 = new UVRect(bb.Min.X, bb.Min.Y,
                                                        bb.Max.X - bb.Min.X,
                                                        bb.Max.Y - bb.Min.Y);
                                    return r2.Intersect(nbrRect).Area > tol;
                                })
                                .OrderByDescending(fr => fr.Id.IntegerValue)
                                .ToList();

                            if (!hits.Any()) continue;
                            neighborCount++;
                            var top = hits[0];
                            var col = view.GetElementOverrides(top.Id).ProjectionLineColor;
                            var room = GlobalData.SavedSpaces
                                .FirstOrDefault(sp => sp.WpfColor.R == col.Red
                                                   && sp.WpfColor.G == col.Green
                                                   && sp.WpfColor.B == col.Blue);
                            if (room != null) rooms.Add(room);
                        }

                        return new { cell, neighborCount, rooms };
                    })
                    // only those with ≥1 neighbour
                    .Where(x => x.neighborCount > 0)
                    .OrderByDescending(x => x.neighborCount)
                    .ToList();

                    if (!scored.Any()) break;

                    var best = scored[0];
                    var winner = best.rooms
                        .OrderByDescending(r => r.SquareTrimmedArea)
                        .First();

                    var region = FilledRegion.Create(
                        _doc, _regionType.Id, view.Id,
                        new List<CurveLoop> { best.cell.Loop }
                    );
                    _view.SetElementOverrides(region.Id,
                        MakeOGS(winner).SetSurfaceTransparency(0));
                    newRegionIds.Add(region.Id);

                    winner.SquareTrimmedArea -= best.cell.Size * best.cell.Size;
                    painted[best.cell] = winner;
                    empties.Remove(best.cell);
                }

                // Pass 2: isolated cells
                foreach (var cell in empties.ToList())
                {
                    var winner = GlobalData.SavedSpaces
                        .OrderByDescending(sp => sp.SquareTrimmedArea)
                        .First();

                    var region = FilledRegion.Create(
                        _doc, _regionType.Id, view.Id,
                        new List<CurveLoop> { cell.Loop }
                    );
                    _view.SetElementOverrides(region.Id,
                        MakeOGS(winner).SetSurfaceTransparency(0));
                    newRegionIds.Add(region.Id);

                    winner.SquareTrimmedArea -= cell.Size * cell.Size;
                    painted[cell] = winner;
                    empties.Remove(cell);
                }

                tx.Commit();
            }

            return newRegionIds;
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



        public void DisplayRoomAreas()
        {
            var sb = new StringBuilder();
            foreach (var space in GlobalData.SavedSpaces)
            {
                // Assuming SpaceNode has a Name property; if not, omit or replace with an identifier
                sb.AppendLine($"{space.Name}: {space.Area:F2}");
            }

            TaskDialog.Show(
                "All Room Areas",
                sb.Length > 0
                    ? sb.ToString()
                    : "No rooms found."
            );
        }





        /// <summary>
        /// After Phase 2, tally each cell by its most-recent top-filled color,
        /// compute new remaining area for each room’s square envelope, and display results.
        /// </summary>
        public void ReportPhase2CellAreas(List<ModuleGridCell> cells)
        {
            // 1) Tally cell areas per room based on most recent top fill
            var areaByRoom = GlobalData.SavedSpaces.ToDictionary(sp => sp, sp => 0.0);
            double tol = _doc.Application.ShortCurveTolerance;

            foreach (var cell in cells)
            {
                var cellRect = new UVRect(cell.OriginX, cell.OriginY, cell.Size, cell.Size);
                var regions = new FilteredElementCollector(_doc, _view.Id)
                    .OfClass(typeof(FilledRegion))
                    .Cast<FilledRegion>()
                    .Where(fr => {
                        var bb = fr.get_BoundingBox(_view);
                        if (bb == null) return false;
                        var r2 = new UVRect(bb.Min.X, bb.Min.Y, bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y);
                        return r2.Intersect(cellRect).Area > tol;
                    }).ToList();
                if (!regions.Any()) continue;

                // pick most recent by largest ID
                var top = regions.OrderByDescending(fr => fr.Id.IntegerValue).First();
                var col = _view.GetElementOverrides(top.Id).ProjectionLineColor;
                var owner = GlobalData.SavedSpaces.FirstOrDefault(sp =>
                    sp.WpfColor.R == col.Red && sp.WpfColor.G == col.Green && sp.WpfColor.B == col.Blue);
                if (owner != null)
                    areaByRoom[owner] += (cell.Size * cell.Size);
            }

            // 2) Compute square bounding area and update trimmed budget
            foreach (var kv in areaByRoom)
            {
                var sp = kv.Key;
                double filledArea = kv.Value;
                // circle area = sp.Area; square side = 2r => squareArea = 4r^2 = (4/π)*circleArea
                double squareArea = (4.0 / Math.PI) * sp.Area;
                double trimmedBudget = squareArea - filledArea;
                sp.SquareTrimmedArea = trimmedBudget;
            }

            //// 3) Display results
            //var sb = new System.Text.StringBuilder();
            //sb.AppendLine("Phase 2: Filled and Remaining Areas");
            //foreach (var sp in GlobalData.SavedSpaces.OrderByDescending(sp => sp.SquareTrimmedArea))
            //{
            //    double filledArea = areaByRoom[sp];
            //    sb.AppendLine($" - {sp.Name}: Filled = {filledArea:F2}, Remaining Budget = {sp.SquareTrimmedArea:F2}");
            //}

            //TaskDialog.Show("Phase 2 Areas Report", sb.ToString());
        }
    }




}


