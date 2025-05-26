using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
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
            public ElementId RegionId;
            public CurveLoop Loop;
        }

        /// <summary>
        /// Clips each space’s 3×3 grid cells against the module arrangement,
        /// draws only the inside pieces, and returns the trimmed‑off areas per cell.
        /// Also accumulates each space’s total trimmed area into SpaceNode.SquareTrimmedArea.
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

        
            // pick a solid drafting fill-pattern
            var fillPattern = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .First(fp =>
                    fp.GetFillPattern().IsSolidFill &&
                    fp.GetFillPattern().Target == FillPatternTarget.Drafting);

            // precompute module rectangles
            var modules = arrangement.PlacedModules
                .Select(pm => new
                {
                    MinX = pm.Origin.X,
                    MinY = pm.Origin.Y,
                    MaxX = pm.Origin.X + pm.ModuleInstance.EffectiveHorizontal,
                    MaxY = pm.Origin.Y + pm.ModuleInstance.EffectiveVertical
                })
                .ToList();

            using (var trans = new Transaction(doc, "Trim & Draw Grids"))
            {
                trans.Start();
                double tol = doc.Application.ShortCurveTolerance;

                foreach (var space in spaces)
                {
                    // compute cell grid geometry
                    double radius = Math.Sqrt(space.Area / Math.PI);
                    double side = 2 * radius;
                    double cellSize = side / 3.0;
                    int nCols = 3, nRows = 3;

                    double cx = space.Position.X;
                    double cy = space.Position.Y;
                    double z = space.Position.Z;
                    double minX = cx - radius;
                    double minY = cy - radius;

                    // create sketch plane (for consistency with old code)
                    var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(minX, minY, z));
                    SketchPlane.Create(doc, plane);

                    // prepare overrides exactly as old code
                    var ogs = new OverrideGraphicSettings()
                        .SetSurfaceForegroundPatternColor(new Color(space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                        .SetSurfaceBackgroundPatternColor(new Color(space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                        .SetSurfaceForegroundPatternId(fillPattern.Id)
                        .SetSurfaceBackgroundPatternId(fillPattern.Id)
                        .SetSurfaceTransparency(50)
                        .SetProjectionLineColor(new Color(space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                        .SetProjectionLineWeight(1);

                    int cellIndex = 0;
                    double cellArea = cellSize * cellSize;

                    // iterate cells
                    for (int i = 0; i < nCols; i++)
                    {
                        for (int j = 0; j < nRows; j++)
                        {
                            double x0 = minX + i * cellSize;
                            double y0 = minY + j * cellSize;
                            double x1 = x0 + cellSize;
                            double y1 = y0 + cellSize;

                            // compute overlaps
                            double insideArea = 0.0;
                            var loops = new List<CurveLoop>();
                            foreach (var m in modules)
                            {
                                double ix0 = Math.Max(x0, m.MinX);
                                double iy0 = Math.Max(y0, m.MinY);
                                double ix1 = Math.Min(x1, m.MaxX);
                                double iy1 = Math.Min(y1, m.MaxY);
                                double w = ix1 - ix0;
                                double h = iy1 - iy0;
                                if (w <= tol || h <= tol) continue;

                                insideArea += w * h;
                                var loop = new CurveLoop();
                                loop.Append(Line.CreateBound(new XYZ(ix0, iy0, z), new XYZ(ix1, iy0, z)));
                                loop.Append(Line.CreateBound(new XYZ(ix1, iy0, z), new XYZ(ix1, iy1, z)));
                                loop.Append(Line.CreateBound(new XYZ(ix1, iy1, z), new XYZ(ix0, iy1, z)));
                                loop.Append(Line.CreateBound(new XYZ(ix0, iy1, z), new XYZ(ix0, iy0, z)));
                                loops.Add(loop);
                            }

                            // draw each interior loop with old coloring
                            foreach (var loop in loops)
                            {
                                var region = FilledRegion.Create(doc, regionType.Id, view.Id, new List<CurveLoop> { loop });
                                view.SetElementOverrides(region.Id, ogs);
                                drawnIds.Add(region.Id);

                                // record each loop’s region for external coloring if needed
                                double trimmed = cellArea - insideArea;
                                trimResults.Add(new TrimResult
                                {
                                    Space = space,
                                    CellIndex = cellIndex,
                                    TrimmedArea = trimmed,
                                    RegionId = region.Id,
                                     Loop = loop
                                });
                            }

                            // if no loops, record trimmed full cell
                            if (loops.Count == 0)
                            {
                                trimResults.Add(new TrimResult
                                {
                                    Space = space,
                                    CellIndex = cellIndex,
                                    TrimmedArea = cellArea,
                                    RegionId = ElementId.InvalidElementId
                                });
                            }

                            cellIndex++;
                        }
                    }
                }

                trans.Commit();
            }

            // update each space’s total trimmed area
            var lookup = trimResults
                .GroupBy(r => r.Space)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.TrimmedArea));
            foreach (var space in spaces)
                space.SquareTrimmedArea = lookup.TryGetValue(space, out double sum) ? sum : 0;

            return drawnIds;
        }










        /// <summary>
        /// Splits each trimmed region by grid cells, recolors them, and builds a mapping of which cells contain which region blocks per room name.
        /// </summary>
        /// <param name="doc">Revit document</param>
        /// <param name="arrangement">Module grid arrangement, with .GridCells each having GlobalIndex and Loop</param>
        /// <param name="trims">List of TrimResult, each containing a Space and a geometry Loop</param>
        /// <param name="transparency">Optional transparency for filled regions</param>
        /// <returns>
        /// A dictionary mapping:
        ///   cellIndex -> (roomName -> list of new region ElementIds)
        /// </returns>
        public Dictionary<int, Dictionary<string, List<ElementId>>> SplitAndRecolorTrimmedRegionsByModuleGrid(
            Document doc,
            ModuleArrangementResult arrangement,
            IList<TrimResult> trims,
            byte transparency = 50)
        {
            // Prepare fill and region types
            var regionType = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .First();

            var fillPattern = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .First(fp => fp.GetFillPattern().IsSolidFill
                           && fp.GetFillPattern().Target == FillPatternTarget.Drafting);

            var view = doc.ActiveView;
            double tol = doc.Application.ShortCurveTolerance;

            // Mapping: cellGlobalIndex -> (roomName -> list of new region Ids)
            var cellRoomRegions = new Dictionary<int, Dictionary<string, List<ElementId>>>();

            // Initialize empty mapping for each cell
            foreach (var cell in arrangement.GridCells)
            {
                cellRoomRegions[cell.GlobalIndex] = new Dictionary<string, List<ElementId>>();
            }

            // Precompute bounding boxes for grid cells
            var cellBBoxes = arrangement.GridCells
                .ToDictionary(c => c.GlobalIndex, c => ComputeBBox(c.Loop));

            var toDelete = new HashSet<ElementId>();

            using (var tx = new Transaction(doc, "Split & Recolor Regions with Mapping by Name"))
            {
                tx.Start();

                foreach (var trim in trims)
                {
                    var oldId = trim.RegionId;
                    if (oldId == ElementId.InvalidElementId || doc.GetElement(oldId) == null)
                        continue;

                    var rBox = ComputeBBox(trim.Loop);
                    double z = trim.Loop.First().GetEndPoint(0).Z;
                    string roomName = trim.Space.Name;

                    foreach (var kv in cellBBoxes)
                    {
                        int cellIdx = kv.Key;
                        var cellBox = kv.Value;

                        // compute intersection extents
                        double ix0 = Math.Max(rBox.Min.X, cellBox.Min.X);
                        double iy0 = Math.Max(rBox.Min.Y, cellBox.Min.Y);
                        double ix1 = Math.Min(rBox.Max.X, cellBox.Max.X);
                        double iy1 = Math.Min(rBox.Max.Y, cellBox.Max.Y);

                        if (ix1 - ix0 <= tol || iy1 - iy0 <= tol)
                            continue;

                        // build clipped loop
                        var loop = new CurveLoop();
                        loop.Append(Line.CreateBound(new XYZ(ix0, iy0, z), new XYZ(ix1, iy0, z)));
                        loop.Append(Line.CreateBound(new XYZ(ix1, iy0, z), new XYZ(ix1, iy1, z)));
                        loop.Append(Line.CreateBound(new XYZ(ix1, iy1, z), new XYZ(ix0, iy1, z)));
                        loop.Append(Line.CreateBound(new XYZ(ix0, iy1, z), new XYZ(ix0, iy0, z)));

                        // create and color new region
                        var newReg = FilledRegion.Create(
                            doc, regionType.Id, view.Id, new List<CurveLoop> { loop });

                        var c = trim.Space.WpfColor;
                        var col = new Autodesk.Revit.DB.Color(c.R, c.G, c.B);
                        var ogs = new OverrideGraphicSettings()
                            .SetSurfaceForegroundPatternColor(col)
                            .SetSurfaceBackgroundPatternColor(col)
                            .SetSurfaceForegroundPatternId(fillPattern.Id)
                            .SetSurfaceBackgroundPatternId(fillPattern.Id)
                            .SetSurfaceTransparency(transparency)
                            .SetProjectionLineColor(col)
                            .SetProjectionLineWeight(1);

                        view.SetElementOverrides(newReg.Id, ogs);

                        // record mapping of this new region to the cell and room name
                        var roomMap = cellRoomRegions[cellIdx];
                        if (!roomMap.ContainsKey(roomName))
                            roomMap[roomName] = new List<ElementId>();
                        roomMap[roomName].Add(newReg.Id);

                        // update trim for downstream use
                        trim.RegionId = newReg.Id;
                    }

                    toDelete.Add(oldId);
                }

                // delete original regions
                foreach (var id in toDelete)
                    if (doc.GetElement(id) != null)
                        doc.Delete(id);

                tx.Commit();
            }

            return cellRoomRegions;
        }


























        // Helper bbox and intersection methods (same as before):
        private BoundingBoxXYZ ComputeBBox(CurveLoop loop)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var c in loop)
                foreach (var p in new[] { c.GetEndPoint(0), c.GetEndPoint(1) })
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }
            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, 0),
                Max = new XYZ(maxX, maxY, 0)
            };
        }

        private bool BBoxesIntersect(BoundingBoxXYZ a, BoundingBoxXYZ b)
            => a.Min.X <= b.Max.X && a.Max.X >= b.Min.X
            && a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y;








        ////// <summary>
        ///// In every module‐cell with ≥2 overlapping spaces,
        ///// replace each room’s single region with:
        /////   • The intersection rectangle, and
        /////   • One region per leftover rectangle slice.
        ///// </summary>
        //public void SplitRegionsIntoIntersectionAndRemainders(
        //    Document doc,
        //    ModuleArrangementResult arrangement,
        //    IList<TrimResult> trims,
        //    byte transparency = 50)
        //{
        //    var view = doc.ActiveView;
        //    double tol = doc.Application.ShortCurveTolerance;

        //    // pick one FilledRegionType + solid drafting fill‐pattern
        //    var regionType = new FilteredElementCollector(doc)
        //        .OfClass(typeof(FilledRegionType))
        //        .Cast<FilledRegionType>()
        //        .First();
        //    var fillPattern = new FilteredElementCollector(doc)
        //        .OfClass(typeof(FillPatternElement))
        //        .Cast<FillPatternElement>()
        //        .First(fp =>
        //            fp.GetFillPattern().IsSolidFill &&
        //            fp.GetFillPattern().Target == FillPatternTarget.Drafting);

        //    // 1) build cell‐bbox map
        //    var cellBBoxes = arrangement.GridCells
        //        .ToDictionary(c => c.GlobalIndex, c => ComputeBBox(c.Loop));

        //    // 2) group trims by cell
        //    var cellGroups = new Dictionary<int, List<TrimResult>>();
        //    foreach (var trim in trims)
        //    {
        //        if (trim.RegionId == ElementId.InvalidElementId
        //            || doc.GetElement(trim.RegionId) == null) continue;

        //        var rBox = ComputeBBox(trim.Loop);
        //        foreach (var kv in cellBBoxes)
        //            if (BBoxesIntersect(rBox, kv.Value))
        //                (cellGroups
        //                    .GetValueOrDefault(kv.Key, new List<TrimResult>()))
        //                    .Add(trim);
        //    }

        //    using (var tx = new Transaction(doc, "Split Intersection + Remainders"))
        //    {
        //        tx.Start();

        //        foreach (var kv in cellGroups)
        //        {
        //            var group = kv.Value;
        //            var distinctSpaces = group.Select(r => r.Space).Distinct().ToList();
        //            if (distinctSpaces.Count < 2) continue;

        //            // compute the common intersection rect of all loops
        //            var boxes = group.Select(r => ComputeBBox(r.Loop)).ToList();
        //            double ix0 = boxes.Max(b => b.Min.X);
        //            double iy0 = boxes.Max(b => b.Min.Y);
        //            double ix1 = boxes.Min(b => b.Max.X);
        //            double iy1 = boxes.Min(b => b.Max.Y);

        //            // skip if no real overlap
        //            if (ix1 - ix0 <= tol || iy1 - iy0 <= tol) continue;

        //            // prepare the overlap loop
        //            double z = group[0].Loop.First().GetEndPoint(0).Z;
        //            var overlapLoop = new CurveLoop();
        //            overlapLoop.Append(Line.CreateBound(new XYZ(ix0, iy0, z), new XYZ(ix1, iy0, z)));
        //            overlapLoop.Append(Line.CreateBound(new XYZ(ix1, iy0, z), new XYZ(ix1, iy1, z)));
        //            overlapLoop.Append(Line.CreateBound(new XYZ(ix1, iy1, z), new XYZ(ix0, iy1, z)));
        //            overlapLoop.Append(Line.CreateBound(new XYZ(ix0, iy1, z), new XYZ(ix0, iy0, z)));

        //            // for each room‐trim in this cell
        //            foreach (var trim in group)
        //            {
        //                var oldId = trim.RegionId;
        //                var outerBox = ComputeBBox(trim.Loop);

        //                // --- 1) draw the intersection piece ---
        //                var interReg = FilledRegion.Create(
        //                    doc,
        //                    regionType.Id,
        //                    view.Id,
        //                    new List<CurveLoop> { overlapLoop });
        //                ApplyOverride(interReg.Id, trim.Space, fillPattern, view, transparency);

        //                // --- 2) carve the remainder into up to 4 rectangles ---
        //                var rects = new List<BoundingBoxXYZ>();

        //                // left strip
        //                if (ix0 > outerBox.Min.X + tol)
        //                    rects.Add(new BoundingBoxXYZ
        //                    {
        //                        Min = new XYZ(outerBox.Min.X, outerBox.Min.Y, 0),
        //                        Max = new XYZ(ix0, outerBox.Max.Y, 0)
        //                    });

        //                // right strip
        //                if (ix1 < outerBox.Max.X - tol)
        //                    rects.Add(new BoundingBoxXYZ
        //                    {
        //                        Min = new XYZ(ix1, outerBox.Min.Y, 0),
        //                        Max = new XYZ(outerBox.Max.X, outerBox.Max.Y, 0)
        //                    });

        //                // bottom strip
        //                if (iy0 > outerBox.Min.Y + tol)
        //                    rects.Add(new BoundingBoxXYZ
        //                    {
        //                        Min = new XYZ(ix0, outerBox.Min.Y, 0),
        //                        Max = new XYZ(ix1, iy0, 0)
        //                    });

        //                // top strip
        //                if (iy1 < outerBox.Max.Y - tol)
        //                    rects.Add(new BoundingBoxXYZ
        //                    {
        //                        Min = new XYZ(ix0, iy1, 0),
        //                        Max = new XYZ(ix1, outerBox.Max.Y, 0)
        //                    });

        //                // draw each leftover rectangle
        //                foreach (var rb in rects)
        //                {
        //                    var loop = new CurveLoop();
        //                    loop.Append(Line.CreateBound(rb.Min, new XYZ(rb.Max.X, rb.Min.Y, z)));
        //                    loop.Append(Line.CreateBound(new XYZ(rb.Max.X, rb.Min.Y, z), rb.Max));
        //                    loop.Append(Line.CreateBound(rb.Max, new XYZ(rb.Min.X, rb.Max.Y, z)));
        //                    loop.Append(Line.CreateBound(new XYZ(rb.Min.X, rb.Max.Y, z), rb.Min));

        //                    var remReg = FilledRegion.Create(
        //                        doc,
        //                        regionType.Id,
        //                        view.Id,
        //                        new List<CurveLoop> { loop });
        //                    ApplyOverride(remReg.Id, trim.Space, fillPattern, view, transparency);
        //                }

        //                // 3) delete the old single‐cell region
        //                if (doc.GetElement(oldId) != null)
        //                    doc.Delete(oldId);

        //                // update TrimResult to point at the “last” region if needed
        //                // (e.g. trims still used downstream)
               
        //            }
        //        }

        //        tx.Commit();
        //    }
        //}


        ///// <summary>
        ///// Helper to color a region with a space’s RGB + transparency.
        ///// </summary>
        //private void ApplyOverride(
        //    ElementId id,
        //    SpaceNode space,
        //    FillPatternElement fillPattern,
        //    View view,
        //    byte transparency)
        //{
        //    var c = space.WpfColor;
        //    var col = new Autodesk.Revit.DB.Color(c.R, c.G, c.B);
        //    var ogs = new OverrideGraphicSettings()
        //        .SetSurfaceForegroundPatternColor(col)
        //        .SetSurfaceBackgroundPatternColor(col)
        //        .SetSurfaceForegroundPatternId(fillPattern.Id)
        //        .SetSurfaceBackgroundPatternId(fillPattern.Id)
        //        .SetSurfaceTransparency(transparency)
        //        .SetProjectionLineColor(col)
        //        .SetProjectionLineWeight(1);
        //    view.SetElementOverrides(id, ogs);
        //}

      



















    }
}
