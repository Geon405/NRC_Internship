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
