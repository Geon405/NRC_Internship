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
        /// Phase 2: Resolve cells contested by multiple spaces.
        /// For each cell overlapped by ≥2 spaces, finds the space with the highest
        /// SquareTrimmedArea, fills the full cell in that color, deducts its extraArea,
        /// and refunds each loser by their overlapArea.
        /// </summary>
        public List<ElementId> ResolveContestedCells(List<ModuleGridCell> cells)
        {
            var regionIds = new List<ElementId>();
            using (var tx = new Transaction(_doc, "Resolve Contested Cells"))
            {
                tx.Start();

                foreach (var cell in cells)
                {
                    // cell bounds
                    double x0 = cell.OriginX, y0 = cell.OriginY;
                    double x1 = x0 + cell.Size, y1 = y0 + cell.Size;

                    // find all spaces overlapping this cell
                    var overlaps = new List<(SpaceNode space, double area)>();
                    foreach (var sp in GlobalData.SavedSpaces)
                    {
                        double r = Math.Sqrt(sp.Area / Math.PI);
                        double minX = sp.Position.X - r, minY = sp.Position.Y - r;
                        double maxX = minX + 2 * r, maxY = minY + 2 * r;

                        double ox = Math.Max(0, Math.Min(x1, maxX) - Math.Max(x0, minX));
                        double oy = Math.Max(0, Math.Min(y1, maxY) - Math.Max(y0, minY));
                        if (ox > 0 && oy > 0)
                            overlaps.Add((sp, ox * oy));
                    }
                    if (overlaps.Count < 2) continue;  // only contested cells

                    // pick winner by highest remaining trimmed area
                    var winner = overlaps.OrderByDescending(o => o.space.SquareTrimmedArea).First();
                    var losers = overlaps.Where(o => o.space != winner.space).ToList();

                    // fill full cell for winner
                    var region = FilledRegion.Create(
                        _doc, _regionType.Id, _view.Id,
                        new List<CurveLoop> { cell.Loop });
                    var ogs = new OverrideGraphicSettings()
                        .SetSurfaceForegroundPatternColor(new Color(
                            winner.space.WpfColor.R,
                            winner.space.WpfColor.G,
                            winner.space.WpfColor.B))
                        .SetSurfaceBackgroundPatternColor(new Color(
                            winner.space.WpfColor.R,
                            winner.space.WpfColor.G,
                            winner.space.WpfColor.B))
                        .SetSurfaceForegroundPatternId(_fillPattern.Id)
                        .SetSurfaceBackgroundPatternId(_fillPattern.Id)
                        .SetSurfaceTransparency(50)
                        .SetProjectionLineColor(new Color(
                            winner.space.WpfColor.R,
                            winner.space.WpfColor.G,
                            winner.space.WpfColor.B))
                        .SetProjectionLineWeight(5);
                    _view.SetElementOverrides(region.Id, ogs);
                    regionIds.Add(region.Id);

                    // adjust trimmed areas
                    double cellArea = cell.Size * cell.Size;
                    double extraArea = cellArea - winner.area;
                    winner.space.SquareTrimmedArea -= extraArea;
                    foreach (var loser in losers)
                        loser.space.SquareTrimmedArea += loser.area;
                }

                tx.Commit();
            }
            return regionIds;
        }


    }


}
