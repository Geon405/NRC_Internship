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


        // Store the overall boundary of the placed modules
        private double _overallMinX;
        private double _overallMinY;
        private double _overallMaxX;
        private double _overallMaxY;

        // Store all placed rectangles (each is an array of 4 XYZ corners)
        private List<XYZ[]> _placedRectangles = new List<XYZ[]>();


        public void GetArrangementBounds(out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = _overallMinX;
            minY = _overallMinY;
            maxX = _overallMaxX;
            maxY = _overallMaxY;
        }

       


        public class Arrangement
        {
            public List<XYZ[]> ModuleRectangles { get; set; }
            public List<ElementId> GridCellIds { get; set; }

            public List<ModuleArrangement.Arrangement> ValidArrangements { get; private set; }
        }

        public List<Arrangement> CreateMultipleSquareLikeArrangements(Document doc, string selectedCombination, List<ModuleType> moduleTypes, int desiredArrangementCount)
        {
            List<Arrangement> allArrangements = new List<Arrangement>();

            // 1. Parse combination and collect modules.
            Dictionary<int, int> typeCounts = ParseCombination(selectedCombination);
            List<ModuleType> modulesToPlace = new List<ModuleType>();
            foreach (var kvp in typeCounts)
            {
                int moduleTypeIndex = kvp.Key;
                int count = kvp.Value;
                ModuleType modType = moduleTypes[moduleTypeIndex];
                for (int i = 0; i < count; i++)
                    modulesToPlace.Add(modType);
            }

            double landWidth = GlobalData.landWidth;
            double landHeight = GlobalData.landHeight;
            Random rnd = new Random();
            int attempts = 0;

            while (allArrangements.Count < desiredArrangementCount && attempts < desiredArrangementCount * 10)
            {
                attempts++;
                List<XYZ[]> placedRectangles = new List<XYZ[]>();
                double offsetX = 0.0, offsetY = 0.0, currentRowHeight = 0.0;
                bool validArrangement = true;

                // 2. Place each module randomly in one of two orientations.
                foreach (ModuleType mod in modulesToPlace)
                {
                    bool useOriginal = rnd.Next(0, 2) == 0;
                    double modX = useOriginal ? mod.Length : mod.Width;
                    double modY = useOriginal ? mod.Width : mod.Length;

                    // Calculate the area of the module and display it.
                    double moduleArea = modX * modY;
                    //TaskDialog.Show("Module Area", $"Module area: {moduleArea:F2} sq. units.");

                    // Check if the module fits in the current row.
                    if (offsetX + modX > landWidth || offsetY + modY > landHeight)
                    {
                        bool newRow = rnd.Next(0, 2) == 0;
                        if (newRow)
                        {
                            offsetY += currentRowHeight;
                            offsetX = 0.0;
                            currentRowHeight = 0.0;
                        }
                        else
                        {
                            offsetX = 0.0;
                            offsetY += currentRowHeight;
                            currentRowHeight = 0.0;
                        }
                        if (offsetX + modX > landWidth || offsetY + modY > landHeight)
                        {
                            validArrangement = false;
                            break;
                        }
                    }

                    // Create rectangle corners.
                    XYZ p1 = new XYZ(offsetX, offsetY, 0);
                    XYZ p2 = new XYZ(offsetX + modX, offsetY, 0);
                    XYZ p3 = new XYZ(offsetX + modX, offsetY + modY, 0);
                    XYZ p4 = new XYZ(offsetX, offsetY + modY, 0);
                    placedRectangles.Add(new XYZ[] { p1, p2, p3, p4 });

                    offsetX += modX;
                    currentRowHeight = (currentRowHeight < 1e-9) ? modY : Math.Max(currentRowHeight, modY);
                }

                if (!validArrangement)
                    continue;

                // 3. Center the layout in the active view.
                CenterFinalOutputInViewBox(placedRectangles, doc.ActiveView.CropBox);

                // 4. Verify that the arrangement is entirely within the crop box.
                BoundingBoxXYZ cropBox = doc.ActiveView.CropBox;
                foreach (XYZ[] rect in placedRectangles)
                {
                    foreach (XYZ pt in rect)
                    {
                        if (pt.X < cropBox.Min.X || pt.X > cropBox.Max.X ||
                            pt.Y < cropBox.Min.Y || pt.Y > cropBox.Max.Y)
                        {
                            validArrangement = false;
                            break;
                        }
                    }
                    if (!validArrangement)
                        break;
                }

                if (!validArrangement)
                    continue;

                // 5. No grid cell creation here.
                List<ElementId> gridCellIds = new List<ElementId>();

                // 6. Save the arrangement.
                Arrangement arr = new Arrangement
                {
                    ModuleRectangles = placedRectangles,
                    GridCellIds = gridCellIds
                };
                allArrangements.Add(arr);
            }

            return allArrangements;
        }



        /// <summary>
        /// Creates grid cells (as detail curves) inside each module rectangle.
        /// </summary>
        private List<ElementId> CreateGridCellsInsideModules(Document doc, List<XYZ[]> placedRectangles)
        {
            List<ElementId> gridElementIds = new List<ElementId>();
            double shortTol = doc.Application.ShortCurveTolerance;
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(0, 0, 0));
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
            SketchPlane sketch = SketchPlane.Create(doc, plane);

            // Process each module separately.
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

                // Compute the number of columns and rows.
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

                        // Define cell corners.
                        XYZ p1 = new XYZ(cellMinX, cellMinY, 0);
                        XYZ p2 = new XYZ(cellMaxX, cellMinY, 0);
                        XYZ p3 = new XYZ(cellMaxX, cellMaxY, 0);
                        XYZ p4 = new XYZ(cellMinX, cellMaxY, 0);

                        // Create edges for the cell.
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
                            // Create detail curves for each edge.
                            DetailCurve dc = doc.Create.NewDetailCurve(doc.ActiveView, edge);
                            doc.ActiveView.SetElementOverrides(dc.Id, ogs);
                            gridElementIds.Add(dc.Id);
                        }
                    }
                }
            }
            return gridElementIds;
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

            // --- Set module shape color to red ---
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(255, 0, 0)); // red
            doc.ActiveView.SetElementOverrides(ds.Id, ogs);

            return ds;
        }








        // <summary>
        /// Returns a list of perimeter edges (Lines) that represent the merged boundary
        /// around all placed modules.
        /// </summary>
        public List<Line> GetPerimeterOutline()
        {
            // 1. Gather all edges as Segment objects
            List<Segment> allEdges = new List<Segment>();
            foreach (XYZ[] rect in _placedRectangles)
            {
                // Each rect has p1, p2, p3, p4 in some order
                XYZ p1 = rect[0];
                XYZ p2 = rect[1];
                XYZ p3 = rect[2];
                XYZ p4 = rect[3];

                allEdges.Add(new Segment(p1, p2));
                allEdges.Add(new Segment(p2, p3));
                allEdges.Add(new Segment(p3, p4));
                allEdges.Add(new Segment(p4, p1));
            }

            // 2. Merge/clean edges (removes overlaps, duplicates, etc.)
            List<Segment> cleanedSegments = SubtractOverlaps(allEdges);

            // 3. Convert them into Revit Line objects
            List<Line> perimeterLines = new List<Line>();
            foreach (Segment seg in cleanedSegments)
            {
                double length = (seg.End - seg.Start).GetLength();
                if (length > 1e-9) // skip tiny segments
                {
                    perimeterLines.Add(Line.CreateBound(seg.Start, seg.End));
                }
            }

            return perimeterLines;
        }

    }
}