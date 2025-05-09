using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PanelizedAndModularFinal
{
    public class GridTrimmer
    {
        public class TrimResult
        {
            public SpaceNode Space;
            public int CellIndex;
            public double TrimmedArea;
            public ElementId RegionId;    // ← added
        }

        /// <summary>
        /// Clips each space’s 3×3 grid cells against the module arrangement,
        /// draws only the inside pieces, and returns the trimmed‐off areas per cell.
        /// Also accumulates each space’s total trimmed area into SpaceNode.SquareTrimmedArea.
        /// Now records each FilledRegion’s ElementId in the TrimResult.
        /// </summary>
        public List<ElementId> DrawTrimmedGrids(
            Document doc,
            ModuleArrangementResult arrangement,
            IList<SpaceNode> spaces,
            out List<TrimResult> trimResults)
        {
            trimResults = new List<TrimResult>();
            var drawnIds = new List<ElementId>();
            var view = doc.ActiveView;

            // pick one FilledRegionType
            var regionType = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .First();

            // pick a drafting fill‐pattern
            var fillPattern = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .First(fp =>
                    fp.GetFillPattern().IsSolidFill &&
                    fp.GetFillPattern().Target == FillPatternTarget.Drafting
                );

            // build module rects
            var modules = arrangement.PlacedModules
                .Select(pm => new {
                    MinX = pm.Origin.X,
                    MinY = pm.Origin.Y,
                    MaxX = pm.Origin.X + pm.ModuleInstance.EffectiveHorizontal,
                    MaxY = pm.Origin.Y + pm.ModuleInstance.EffectiveVertical
                })
                .ToList();

            using (var t = new Transaction(doc, "Trim & Draw Grids"))
            {
                t.Start();

                foreach (var space in spaces)
                {
                    double radius = Math.Sqrt(space.Area / Math.PI);
                    double cx = space.Position.X;
                    double cy = space.Position.Y;
                    double z = space.Position.Z;
                    double side = 2 * radius;
                    double minX = cx - radius;
                    double minY = cy - radius;

                    int nCols = 3, nRows = 3;
                    double cellSize = side / 3.0;
                    double cellArea = cellSize * cellSize;

                    // sketch plane & initial OGS
                    var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(minX, minY, z));
                    SketchPlane.Create(doc, plane);
                    var ogs = new OverrideGraphicSettings()
                        .SetSurfaceForegroundPatternColor(new Autodesk.Revit.DB.Color(
                            space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                        .SetSurfaceBackgroundPatternColor(new Autodesk.Revit.DB.Color(
                            space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                        .SetSurfaceForegroundPatternId(fillPattern.Id)
                        .SetSurfaceBackgroundPatternId(fillPattern.Id)
                        .SetSurfaceTransparency(50)
                        .SetProjectionLineColor(new Autodesk.Revit.DB.Color(
                            space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                        .SetProjectionLineWeight(1);

                    int cellIndex = 0;
                    foreach (int i in Enumerable.Range(0, nCols))
                        foreach (int j in Enumerable.Range(0, nRows))
                        {
                            double x0 = minX + i * cellSize;
                            double y0 = minY + j * cellSize;
                            double x1 = x0 + cellSize;
                            double y1 = y0 + cellSize;

                            // compute overlap loops
                            double insideArea = 0.0;
                            var loops = new List<CurveLoop>();
                            foreach (var m in modules)
                            {
                                double ix0 = Math.Max(x0, m.MinX);
                                double iy0 = Math.Max(y0, m.MinY);
                                double ix1 = Math.Min(x1, m.MaxX);
                                double iy1 = Math.Min(y1, m.MaxY);
                                if (ix1 > ix0 && iy1 > iy0)
                                {
                                    insideArea += (ix1 - ix0) * (iy1 - iy0);
                                    var loop = new CurveLoop();
                                    loop.Append(Line.CreateBound(new XYZ(ix0, iy0, z), new XYZ(ix1, iy0, z)));
                                    loop.Append(Line.CreateBound(new XYZ(ix1, iy0, z), new XYZ(ix1, iy1, z)));
                                    loop.Append(Line.CreateBound(new XYZ(ix1, iy1, z), new XYZ(ix0, iy1, z)));
                                    loop.Append(Line.CreateBound(new XYZ(ix0, iy1, z), new XYZ(ix0, iy0, z)));
                                    loops.Add(loop);
                                }
                            }

                            double trimmed = cellArea - insideArea;

                            // draw & record each trimmed‐area region
                            foreach (var loop in loops)
                            {
                                var region = FilledRegion.Create(
                                    doc,
                                    regionType.Id,
                                    view.Id,
                                    new List<CurveLoop> { loop }
                                );
                                drawnIds.Add(region.Id);

                                trimResults.Add(new TrimResult
                                {
                                    Space = space,
                                    CellIndex = cellIndex,
                                    TrimmedArea = trimmed,
                                    RegionId = region.Id    // ← populated
                                });

                                view.SetElementOverrides(region.Id, ogs);
                            }

                            cellIndex++;
                        }
                }

                t.Commit();
            }

            // accumulate total trimmed per space
            foreach (var group in trimResults.GroupBy(r => r.Space))
                group.Key.SquareTrimmedArea = group.Sum(r => r.TrimmedArea);

            return drawnIds;
        }
    }
}
