using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PanelizedAndModularFinal
{
    public class ModuleArrangement
    {
        public List<ElementId> SavedBoundaryElementIds { get; private set; }
        public List<ElementId> SavedGridElementIds { get; private set; }
        public XYZ OverallCenter { get; private set; }

        public void CreateSquareLikeArrangement(Document doc, string selectedCombination, List<ModuleType> moduleTypes)
        {
            Dictionary<int, int> typeCounts = ParseCombination(selectedCombination);
            List<ModuleType> modulesToPlace = new List<ModuleType>();

            // Using global land dimensions for initial placement (if needed)
            double landWidth = GlobalData.landWidth;
            double landHeight = GlobalData.landHeight;

            // 1. Collect modules from combination string
            foreach (var kvp in typeCounts)
            {
                int moduleTypeIndex = kvp.Key;
                int count = kvp.Value;
                ModuleType modType = moduleTypes[moduleTypeIndex];
                for (int i = 0; i < count; i++)
                {
                    modulesToPlace.Add(modType);
                }
            }

            // 2. Prepare placement
            double offsetX = 0.0;
            double offsetY = 0.0;
            double currentRowHeight = 0.0;
            List<XYZ[]> placedRectangles = new List<XYZ[]>();

            // 3. Place modules
            foreach (var mod in modulesToPlace)
            {
                double dimX1 = mod.Length;
                double dimY1 = mod.Width;
                double dimX2 = mod.Width;
                double dimY2 = mod.Length;

                bool placed = false;
                double chosenX = 0, chosenY = 0;

                bool FitsInRow(double testX, double testY)
                {
                    if (offsetX + testX > landWidth) return false;
                    if (currentRowHeight > 0 && Math.Abs(testY - currentRowHeight) > 1e-9) return false;
                    if (offsetY + testY > landHeight) return false;
                    return true;
                }

                // Default orientation
                if (!placed && FitsInRow(dimX1, dimY1))
                {
                    chosenX = dimX1;
                    chosenY = dimY1;
                    placed = true;
                }
                // Rotated orientation
                if (!placed && FitsInRow(dimX2, dimY2))
                {
                    chosenX = dimX2;
                    chosenY = dimY2;
                    placed = true;
                }

                // Start new row if needed
                if (!placed)
                {
                    offsetY += currentRowHeight;
                    offsetX = 0.0;
                    currentRowHeight = 0.0;

                    if (FitsInRow(dimX1, dimY1))
                    {
                        chosenX = dimX1;
                        chosenY = dimY1;
                        placed = true;
                    }
                    else if (FitsInRow(dimX2, dimY2))
                    {
                        chosenX = dimX2;
                        chosenY = dimY2;
                        placed = true;
                    }
                    else
                    {
                        throw new Exception("Module doesn't fit in new row.");
                    }
                }

                // Record rectangle corners (p1: lower left, p2: lower right, p3: upper right, p4: upper left)
                XYZ p1 = new XYZ(offsetX, offsetY, 0);
                XYZ p2 = new XYZ(offsetX + chosenX, offsetY, 0);
                XYZ p3 = new XYZ(offsetX + chosenX, offsetY + chosenY, 0);
                XYZ p4 = new XYZ(offsetX, offsetY + chosenY, 0);
                placedRectangles.Add(new XYZ[] { p1, p2, p3, p4 });

                offsetX += chosenX;
                if (Math.Abs(currentRowHeight) < 1e-9)
                    currentRowHeight = chosenY;
            }

            // 4. Center the layout in the active view's crop box
            BoundingBoxXYZ cropBox = doc.ActiveView.CropBox;
            CenterFinalOutputInViewBox(placedRectangles, cropBox);

            // 5. Transaction to build geometry + grid
            using (Transaction trans = new Transaction(doc, "Create Modules + Grid"))
            {
                trans.Start();

                // Create each module solid
                foreach (var rect in placedRectangles)
                {
                    CreateModuleSolid(doc, rect[0], rect[1], rect[2], rect[3], 1.0);
                }

                // Create red boundary
                //double gap = 1;
                //SavedBoundaryElementIds = CreateOrthogonalBoundary(doc, placedRectangles, gap, 0, 0);

                // Create grid cells inside modules
                double cellSize = ComputeCellSize(placedRectangles);
                SavedGridElementIds = CreateGridCellsInsideModules(doc, placedRectangles);

                trans.Commit();
            }

            // 6. Compute overall center of the final output
            OverallCenter = ComputeFinalOutputCenter(placedRectangles);
        }

        /// <summary>
        /// Centers the layout (placedRectangles) within the provided view box.
        /// </summary>
        private void CenterFinalOutputInViewBox(List<XYZ[]> placedRectangles, BoundingBoxXYZ viewBox)
        {
            // Calculate view center using the crop box boundaries
            XYZ viewCenter = new XYZ((viewBox.Min.X + viewBox.Max.X) / 2.0,
                                     (viewBox.Min.Y + viewBox.Max.Y) / 2.0, 0);
            // Compute current layout center
            XYZ layoutCenter = ComputeFinalOutputCenter(placedRectangles);
            // Determine offset
            XYZ offset = viewCenter - layoutCenter;

            // Apply the offset to each rectangle's corners
            for (int i = 0; i < placedRectangles.Count; i++)
            {
                for (int j = 0; j < placedRectangles[i].Length; j++)
                {
                    placedRectangles[i][j] = placedRectangles[i][j] + offset;
                }
            }
        }

        /// <summary>
        /// Computes the center of the bounding rectangle around all placed module rectangles.
        /// </summary>
        private XYZ ComputeFinalOutputCenter(List<XYZ[]> placedRectangles)
        {
            double overallMinX = double.MaxValue;
            double overallMinY = double.MaxValue;
            double overallMaxX = double.MinValue;
            double overallMaxY = double.MinValue;

            foreach (XYZ[] rect in placedRectangles)
            {
                double minX = Math.Min(rect[0].X, rect[2].X);
                double minY = Math.Min(rect[0].Y, rect[2].Y);
                double maxX = Math.Max(rect[0].X, rect[2].X);
                double maxY = Math.Max(rect[0].Y, rect[2].Y);

                overallMinX = Math.Min(overallMinX, minX);
                overallMinY = Math.Min(overallMinY, minY);
                overallMaxX = Math.Max(overallMaxX, maxX);
                overallMaxY = Math.Max(overallMaxY, maxY);
            }

            double centerX = (overallMinX + overallMaxX) / 2;
            double centerY = (overallMinY + overallMaxY) / 2;
            return new XYZ(centerX, centerY, 0);
        }

        private double ComputeCellSize(List<XYZ[]> placedRectangles)
        {
            double minDimension = double.MaxValue;
            foreach (var rect in placedRectangles)
            {
                double width = Math.Abs(rect[1].X - rect[0].X);
                double height = Math.Abs(rect[3].Y - rect[0].Y);
                double localMin = Math.Min(width, height);
                if (localMin < minDimension)
                    minDimension = localMin;

            }
            return minDimension / 3.0;
        }

        private List<ElementId> CreateGridCellsInsideModules(Document doc, List<XYZ[]> placedRectangles)
        {
            List<ElementId> gridElementIds = new List<ElementId>();
            double shortTol = doc.Application.ShortCurveTolerance;
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(0, 0, 255));
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
            SketchPlane sketch = SketchPlane.Create(doc, plane);

            // Process each module separately
            foreach (XYZ[] rect in placedRectangles)
            {
                // Determine module boundaries.
                double minX = Math.Min(rect[0].X, rect[2].X);
                double minY = Math.Min(rect[0].Y, rect[2].Y);
                double maxX = Math.Max(rect[0].X, rect[2].X);
                double maxY = Math.Max(rect[0].Y, rect[2].Y);
                double width = maxX - minX;
                double height = maxY - minY;

                // Use the smaller dimension to define a square cell size.
                double cellSize = Math.Min(width, height) / 3.0;

                // Compute the number of columns and rows needed to cover the entire rectangle.
                int nCols = (int)Math.Round(width / cellSize);
                int nRows = (int)Math.Round(height / cellSize);

                for (int i = 0; i < nCols; i++)
                {
                    for (int j = 0; j < nRows; j++)
                    {
                        double cellMinX = minX + i * cellSize;
                        double cellMinY = minY + j * cellSize;
                        double cellMaxX = cellMinX + cellSize;
                        double cellMaxY = cellMinY + cellSize;

                        // Create cell geometry
                        XYZ p1 = new XYZ(cellMinX, cellMinY, 0);
                        XYZ p2 = new XYZ(cellMaxX, cellMinY, 0);
                        XYZ p3 = new XYZ(cellMaxX, cellMaxY, 0);
                        XYZ p4 = new XYZ(cellMinX, cellMaxY, 0);

                        List<Line> edges = new List<Line>
                {
                    Line.CreateBound(p1, p2),
                    Line.CreateBound(p2, p3),
                    Line.CreateBound(p3, p4),
                    Line.CreateBound(p4, p1)
                };

                        foreach (Line edge in edges)
                        {
                            if (edge.Length < shortTol) continue;
                            DetailCurve dc = doc.Create.NewDetailCurve(doc.ActiveView, edge);
                            doc.ActiveView.SetElementOverrides(dc.Id, ogs);
                            gridElementIds.Add(dc.Id);
                        }
                    }
                }
            }
            return gridElementIds;
        }




        private bool IsCellFullyInsideAnyModule(double cellMinX, double cellMinY, double cellMaxX, double cellMaxY, List<XYZ[]> placedRectangles)
        {
            foreach (XYZ[] rect in placedRectangles)
            {
                double minX = Math.Min(rect[0].X, rect[2].X);
                double minY = Math.Min(rect[0].Y, rect[2].Y);
                double maxX = Math.Max(rect[0].X, rect[2].X);
                double maxY = Math.Max(rect[0].Y, rect[2].Y);

                if (cellMinX >= minX && cellMaxX <= maxX &&
                    cellMinY >= minY && cellMaxY <= maxY)
                {
                    return true;
                }
            }
            return false;
        }

        private void CreateModuleSolid(Document doc, XYZ p1, XYZ p2, XYZ p3, XYZ p4, double height)
        {
            List<Curve> edges = new List<Curve>
            {
                Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p4),
                Line.CreateBound(p4, p1)
            };
            CurveLoop loop = new CurveLoop();
            foreach (Curve c in edges)
                loop.Append(c);

            Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                XYZ.BasisZ,
                height);

            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            ds.ApplicationId = "ModuleArrangement";
            ds.ApplicationDataId = Guid.NewGuid().ToString();
            ds.SetShape(new List<GeometryObject> { solid });
        }

        private Dictionary<int, int> ParseCombination(string combo)
        {
            Dictionary<int, int> result = new Dictionary<int, int>();
            string[] parts = combo.Split('=');
            string modulesPart = parts[0];

            var pattern = new Regex(@"(\d+)\s*x\s*Module_Type\s*(\d+)");
            var matches = pattern.Matches(modulesPart);

            foreach (Match match in matches)
            {
                int count = int.Parse(match.Groups[1].Value);
                int modIndex = int.Parse(match.Groups[2].Value) - 1;
                if (!result.ContainsKey(modIndex))
                    result[modIndex] = 0;
                result[modIndex] += count;
            }
            return result;
        }

        private List<ElementId> CreateOrthogonalBoundary(Document doc, List<XYZ[]> placedRectangles, double gap, double shiftX, double shiftY)
        {
            List<Segment> allEdges = new List<Segment>();
            foreach (var rect in placedRectangles)
            {
                XYZ p1 = rect[0].Add(new XYZ(shiftX, shiftY, 0));
                XYZ p2 = rect[1].Add(new XYZ(shiftX, shiftY, 0));
                XYZ p3 = rect[2].Add(new XYZ(shiftX, shiftY, 0));
                XYZ p4 = rect[3].Add(new XYZ(shiftX, shiftY, 0));

                double minX = Math.Min(p1.X, p3.X);
                double maxX = Math.Max(p1.X, p3.X);
                double minY = Math.Min(p1.Y, p3.Y);
                double maxY = Math.Max(p1.Y, p3.Y);

                allEdges.Add(new Segment(new XYZ(minX, minY, 0), new XYZ(maxX, minY, 0)));
                allEdges.Add(new Segment(new XYZ(minX, maxY, 0), new XYZ(maxX, maxY, 0)));
                allEdges.Add(new Segment(new XYZ(minX, minY, 0), new XYZ(minX, maxY, 0)));
                allEdges.Add(new Segment(new XYZ(maxX, minY, 0), new XYZ(maxX, maxY, 0)));
            }

            List<Segment> boundarySegments = SubtractOverlaps(allEdges);
            List<Segment> offsetSegments = new List<Segment>();
            double shortTol = doc.Application.ShortCurveTolerance;

            foreach (Segment seg in boundarySegments)
            {
                bool horizontal = Math.Abs(seg.Start.Y - seg.End.Y) < 1e-9;
                XYZ mid = (seg.Start + seg.End) / 2.0;
                XYZ normal = horizontal ? new XYZ(0, 1, 0) : new XYZ(1, 0, 0);

                if (IsPointInsideAnyRect(mid + normal * 0.01, placedRectangles, shiftX, shiftY))
                    normal = normal.Negate();

                XYZ offStart = seg.Start + normal * gap;
                XYZ offEnd = seg.End + normal * gap;

                if ((offEnd - offStart).GetLength() > shortTol)
                    offsetSegments.Add(new Segment(offStart, offEnd));
            }

            Plane boundaryPlane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
            SketchPlane boundarySketch = SketchPlane.Create(doc, boundaryPlane);
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(255, 0, 0));

            List<ElementId> boundaryDetailCurveIds = new List<ElementId>();
            foreach (Segment seg in offsetSegments)
            {
                double length = (seg.End - seg.Start).GetLength();
                if (length < shortTol) continue;

                Line line = Line.CreateBound(seg.Start, seg.End);
                if (line.Length < shortTol) continue;

                DetailCurve dc = doc.Create.NewDetailCurve(doc.ActiveView, line);
                doc.ActiveView.SetElementOverrides(dc.Id, ogs);
                boundaryDetailCurveIds.Add(dc.Id);
            }
            return boundaryDetailCurveIds;
        }

        private bool IsPointInsideAnyRect(XYZ pt, List<XYZ[]> rects, double shiftX, double shiftY)
        {
            foreach (var rect in rects)
            {
                XYZ p1 = rect[0].Add(new XYZ(shiftX, shiftY, 0));
                XYZ p3 = rect[2].Add(new XYZ(shiftX, shiftY, 0));

                double minX = Math.Min(p1.X, p3.X);
                double maxX = Math.Max(p1.X, p3.X);
                double minY = Math.Min(p1.Y, p3.Y);
                double maxY = Math.Max(p1.Y, p3.Y);

                if (pt.X >= minX - 1e-9 && pt.X <= maxX + 1e-9 &&
                    pt.Y >= minY - 1e-9 && pt.Y <= maxY + 1e-9)
                {
                    return true;
                }
            }
            return false;
        }

        private List<Segment> SubtractOverlaps(List<Segment> allEdges)
        {
            List<Segment> result = new List<Segment>(allEdges);
            for (int i = 0; i < result.Count; i++)
            {
                for (int j = i + 1; j < result.Count; j++)
                {
                    Segment s1 = result[i];
                    Segment s2 = result[j];

                    bool s1Horizontal = Math.Abs(s1.Start.Y - s1.End.Y) < 1e-9;
                    bool s2Horizontal = Math.Abs(s2.Start.Y - s2.End.Y) < 1e-9;
                    if (s1Horizontal != s2Horizontal)
                        continue;

                    if (s1Horizontal)
                    {
                        if (Math.Abs(s1.Start.Y - s2.Start.Y) > 1e-9) continue;

                        double s1Start = s1.Start.X;
                        double s1End = s1.End.X;
                        double s2Start = s2.Start.X;
                        double s2End = s2.End.X;
                        if (s1End < s2Start || s2End < s1Start) continue;

                        result[i] = SubtractOverlap1D(s1, s2);
                        result[j] = SubtractOverlap1D(s2, s1);
                    }
                    else
                    {
                        if (Math.Abs(s1.Start.X - s2.Start.X) > 1e-9) continue;

                        double s1Start = s1.Start.Y;
                        double s1End = s1.End.Y;
                        double s2Start = s2.Start.Y;
                        double s2End = s2.End.Y;
                        if (s1End < s2Start || s2End < s1Start) continue;

                        result[i] = SubtractOverlap1D(s1, s2);
                        result[j] = SubtractOverlap1D(s2, s1);
                    }
                }
            }
            return result.Where(s => GetLength(s) > 1e-9).ToList();
        }

        private Segment SubtractOverlap1D(Segment s1, Segment s2)
        {
            bool horizontal = Math.Abs(s1.Start.Y - s1.End.Y) < 1e-9;
            if (horizontal)
            {
                double y = s1.Start.Y;
                double s1Start = s1.Start.X;
                double s1End = s1.End.X;
                double s2Start = s2.Start.X;
                double s2End = s2.End.X;

                double overlapStart = Math.Max(s1Start, s2Start);
                double overlapEnd = Math.Min(s1End, s2End);

                if (overlapEnd <= overlapStart) return s1;
                else if (overlapStart <= s1Start && overlapEnd >= s1End)
                    return new Segment(new XYZ(s1Start, y, 0), new XYZ(s1Start, y, 0));
                else if (overlapStart <= s1Start && overlapEnd < s1End)
                    return new Segment(new XYZ(overlapEnd, y, 0), new XYZ(s1End, y, 0));
                else if (overlapStart > s1Start && overlapEnd >= s1End)
                    return new Segment(new XYZ(s1Start, y, 0), new XYZ(overlapStart, y, 0));
                else
                    return new Segment(new XYZ(s1Start, y, 0), new XYZ(overlapStart, y, 0));
            }
            else
            {
                double x = s1.Start.X;
                double s1Start = s1.Start.Y;
                double s1End = s1.End.Y;
                double s2Start = s2.Start.Y;
                double s2End = s2.End.Y;

                double overlapStart = Math.Max(s1Start, s2Start);
                double overlapEnd = Math.Min(s1End, s2End);

                if (overlapEnd <= overlapStart) return s1;
                else if (overlapStart <= s1Start && overlapEnd >= s1End)
                    return new Segment(new XYZ(x, s1Start, 0), new XYZ(x, s1Start, 0));
                else if (overlapStart <= s1Start && overlapEnd < s1End)
                    return new Segment(new XYZ(x, overlapEnd, 0), new XYZ(x, s1End, 0));
                else if (overlapStart > s1Start && overlapEnd >= s1End)
                    return new Segment(new XYZ(x, s1Start, 0), new XYZ(x, overlapStart, 0));
                else
                    return new Segment(new XYZ(x, s1Start, 0), new XYZ(x, overlapStart, 0));
            }
        }



        public List<ElementId> DisplayModuleCombination(Document doc, string selectedCombination, List<ModuleType> moduleTypes)
        {
            List<ElementId> previewElementIds = new List<ElementId>();

            // --- Step 1: Parse Combination and Collect Modules ---
            Dictionary<int, int> typeCounts = ParseCombination(selectedCombination);
            List<ModuleType> modulesToPlace = new List<ModuleType>();
            double landWidth = GlobalData.landWidth;
            double landHeight = GlobalData.landHeight;
            foreach (var kvp in typeCounts)
            {
                int moduleTypeIndex = kvp.Key;
                int count = kvp.Value;
                ModuleType modType = moduleTypes[moduleTypeIndex];
                for (int i = 0; i < count; i++)
                    modulesToPlace.Add(modType);
            }

            // --- Step 2: Determine Module Placement ---
            double offsetX = 0.0, offsetY = 0.0, currentRowHeight = 0.0;
            List<XYZ[]> placedRectangles = new List<XYZ[]>();
            foreach (var mod in modulesToPlace)
            {
                double dimX1 = mod.Length, dimY1 = mod.Width;
                double dimX2 = mod.Width, dimY2 = mod.Length;
                bool placed = false;
                double chosenX = 0, chosenY = 0;

                bool FitsInRow(double testX, double testY)
                {
                    if (offsetX + testX > landWidth) return false;
                    if (currentRowHeight > 0 && Math.Abs(testY - currentRowHeight) > 1e-9) return false;
                    if (offsetY + testY > landHeight) return false;
                    return true;
                }

                // Default orientation
                if (!placed && FitsInRow(dimX1, dimY1))
                {
                    chosenX = dimX1; chosenY = dimY1; placed = true;
                }
                // Rotated orientation
                if (!placed && FitsInRow(dimX2, dimY2))
                {
                    chosenX = dimX2; chosenY = dimY2; placed = true;
                }
                // New row if needed
                if (!placed)
                {
                    offsetY += currentRowHeight;
                    offsetX = 0.0;
                    currentRowHeight = 0.0;
                    if (FitsInRow(dimX1, dimY1))
                    {
                        chosenX = dimX1; chosenY = dimY1; placed = true;
                    }
                    else if (FitsInRow(dimX2, dimY2))
                    {
                        chosenX = dimX2; chosenY = dimY2; placed = true;
                    }
                    else
                        throw new Exception("Module doesn't fit in new row.");
                }

                // Record rectangle corners.
                XYZ p1 = new XYZ(offsetX, offsetY, 0);
                XYZ p2 = new XYZ(offsetX + chosenX, offsetY, 0);
                XYZ p3 = new XYZ(offsetX + chosenX, offsetY + chosenY, 0);
                XYZ p4 = new XYZ(offsetX, offsetY + chosenY, 0);
                placedRectangles.Add(new XYZ[] { p1, p2, p3, p4 });

                offsetX += chosenX;
                if (Math.Abs(currentRowHeight) < 1e-9)
                    currentRowHeight = chosenY;
            }

            // --- Step 3: Center the Layout ---
            CenterFinalOutputInViewBox(placedRectangles, doc.ActiveView.CropBox);

            // --- Step 4: Create Module Solids for Preview and Store their IDs ---
            using (Transaction trans = new Transaction(doc, "Display Module Arrangement"))
            {
                trans.Start();
                foreach (var rect in placedRectangles)
                {
                    DirectShape ds = CreateModuleSolidAndReturn(doc, rect[0], rect[1], rect[2], rect[3], 1.0);
                    previewElementIds.Add(ds.Id);
                }
                trans.Commit();
            }
            OverallCenter = ComputeFinalOutputCenter(placedRectangles);
            return previewElementIds;
        }

        private DirectShape CreateModuleSolidAndReturn(Document doc, XYZ p1, XYZ p2, XYZ p3, XYZ p4, double height)
        {
            List<Curve> edges = new List<Curve>
    {
        Line.CreateBound(p1, p2),
        Line.CreateBound(p2, p3),
        Line.CreateBound(p3, p4),
        Line.CreateBound(p4, p1)
    };

            CurveLoop loop = new CurveLoop();
            foreach (Curve c in edges)
                loop.Append(c);

            Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                XYZ.BasisZ,
                height);

            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            ds.ApplicationId = "ModuleArrangement";
            ds.ApplicationDataId = Guid.NewGuid().ToString();
            ds.SetShape(new List<GeometryObject> { solid });

            return ds;
        }















        private double GetLength(Segment s)
        {
            return Math.Abs(s.Start.X - s.End.X) < 1e-9
                ? Math.Abs(s.End.Y - s.Start.Y)
                : Math.Abs(s.End.X - s.Start.X);
        }

        private struct Segment
        {
            public XYZ Start;
            public XYZ End;
            public Segment(XYZ s, XYZ e)
            {
                if (Math.Abs(s.X - e.X) < 1e-9)
                {
                    if (s.Y > e.Y) { var tmp = s; s = e; e = tmp; }
                }
                else
                {
                    if (s.X > e.X) { var tmp = s; s = e; e = tmp; }
                }
                Start = s;
                End = e;
            }
        }
    }
}