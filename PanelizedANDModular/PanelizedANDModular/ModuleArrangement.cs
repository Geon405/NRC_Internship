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
        /// <summary>
        /// Arranges modules in a square-like footprint (alternating horizontal & vertical rows)
        /// and creates each module as a rectangular solid.
        /// </summary>
        // --- In ModuleArrangement.cs ---
        // --- In ModuleArrangement.cs ---
        public void CreateSquareLikeArrangement(Document doc, string selectedCombination, List<ModuleType> moduleTypes)
        {
            // 1. Parse combination string and prepare module list.
            Dictionary<int, int> typeCounts = ParseCombination(selectedCombination);
            List<ModuleType> modulesToPlace = new List<ModuleType>();
            double totalArea = 0.0;

            double landWidth = GlobalData.landWidth;
            double landHeight = GlobalData.landHeight;

            foreach (var kvp in typeCounts)
            {
                int moduleTypeIndex = kvp.Key;  // 0-based index
                int count = kvp.Value;
                ModuleType modType = moduleTypes[moduleTypeIndex];
                for (int i = 0; i < count; i++)
                {
                    modulesToPlace.Add(modType);
                    totalArea += modType.Area;
                }
            }

            // 2. Prepare for module placement within the land area.
            double offsetX = 0.0;
            double offsetY = 0.0;
            double currentRowHeight = 0.0;
            List<XYZ[]> placedRectangles = new List<XYZ[]>();

            // 3. Place each module, enforcing same-height rows to avoid corner adjacency.
            foreach (var mod in modulesToPlace)
            {
                // Try default and rotated orientation.
                double dimX1 = mod.Length;
                double dimY1 = mod.Width;
                double dimX2 = mod.Width;
                double dimY2 = mod.Length;

                // Decide which orientation fits best in leftover horizontal space.
                // For simplicity, pick the first orientation that fits horizontally:
                bool placed = false;
                double chosenX = 0, chosenY = 0;

                // Helper function to check fit:
                bool FitsInRow(double testX, double testY)
                {
                    // Must fit horizontally
                    if (offsetX + testX > landWidth) return false;
                    // Must match row height if row is not empty
                    if (currentRowHeight > 0 && Math.Abs(testY - currentRowHeight) > 1e-9) return false;
                    // Must fit vertically if it’s the first in a new row
                    if (offsetY + testY > landHeight) return false;
                    return true;
                }

                // 3a. Check default orientation
                if (!placed && FitsInRow(dimX1, dimY1))
                {
                    chosenX = dimX1;
                    chosenY = dimY1;
                    placed = true;
                }
                // 3b. Check rotated orientation
                if (!placed && FitsInRow(dimX2, dimY2))
                {
                    chosenX = dimX2;
                    chosenY = dimY2;
                    placed = true;
                }

                // 3c. If neither orientation fits in current row, move to next row.
                if (!placed)
                {
                    // Advance to next row
                    offsetY += currentRowHeight;
                    offsetX = 0.0;
                    currentRowHeight = 0.0;

                    // Try again in the new row
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
                        throw new Exception("Module doesn't fit in the new row; land area too small.");
                    }
                }

                // 3d. Place the module in the current row
                XYZ p1 = new XYZ(offsetX, offsetY, 0);
                XYZ p2 = new XYZ(offsetX + chosenX, offsetY, 0);
                XYZ p3 = new XYZ(offsetX + chosenX, offsetY + chosenY, 0);
                XYZ p4 = new XYZ(offsetX, offsetY + chosenY, 0);
                placedRectangles.Add(new XYZ[] { p1, p2, p3, p4 });

                // Update offsets
                offsetX += chosenX;
                // If this is the first module in the row, set row height.
                // Otherwise, row height remains the same.
                if (Math.Abs(currentRowHeight) < 1e-9)
                {
                    currentRowHeight = chosenY;
                }
            }

            // 4. Build geometry in a Revit transaction.
            using (Transaction trans = new Transaction(doc, "Create Module Arrangement"))
            {
                trans.Start();

                // Create each module as an extruded solid (height = 1 ft).
                foreach (var rect in placedRectangles)
                {
                    CreateModuleSolid(doc, rect[0], rect[1], rect[2], rect[3], 1.0);
                }

                // Create boundary in red, offset outward by 1 ft.
                double gap = 1;
                CreateOrthogonalBoundary(doc, placedRectangles, gap, 0, 0);

                trans.Commit();
            }
        }





        /// <summary>
        /// Creates a rectangular extrusion (DirectShape) to represent the module in 3D.
        /// 'height' is the extrusion distance.
        /// </summary>
        private void CreateModuleSolid(Document doc, XYZ p1, XYZ p2, XYZ p3, XYZ p4, double height)
        {
            // Build a closed CurveLoop for the rectangle base.
            List<Curve> edges = new List<Curve>
            {
                Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p4),
                Line.CreateBound(p4, p1)
            };
            CurveLoop loop = new CurveLoop();
            foreach (Curve c in edges)
            {
                loop.Append(c);
            }

            // Extrude the loop upward (Z direction).
            Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                XYZ.BasisZ,
                height);

            // Create a DirectShape element in the Generic Models category.
            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            ds.ApplicationId = "ModuleArrangement";
            ds.ApplicationDataId = Guid.NewGuid().ToString();
            ds.SetShape(new List<GeometryObject> { solid });
        }

        /// <summary>
        /// Parses a string like "2 x Module_Type 1 + 1 x Module_Type 2 = 300 ft²"
        /// and returns a dictionary of (moduleIndex -> count).
        /// </summary>
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

        /// <summary>
        /// Creates an orthogonal boundary (only vertical/horizontal lines) around
        /// the union of the placed rectangles, offset outward by a given gap.
        /// If two rectangles share an edge, that portion is omitted to avoid diagonals.
        /// </summary>
        private void CreateOrthogonalBoundary(
            Document doc,
            List<XYZ[]> placedRectangles,
            double gap,
            double shiftX,
            double shiftY)
        {
            // 1. Build a list of all rectangle edges (horizontal or vertical).
            List<Segment> allEdges = new List<Segment>();
            foreach (var rect in placedRectangles)
            {
                // Shift corners to final position
                XYZ p1 = rect[0].Add(new XYZ(shiftX, shiftY, 0)); // bottom-left
                XYZ p2 = rect[1].Add(new XYZ(shiftX, shiftY, 0)); // bottom-right
                XYZ p3 = rect[2].Add(new XYZ(shiftX, shiftY, 0)); // top-right
                XYZ p4 = rect[3].Add(new XYZ(shiftX, shiftY, 0)); // top-left

                double minX = Math.Min(p1.X, p3.X);
                double maxX = Math.Max(p1.X, p3.X);
                double minY = Math.Min(p1.Y, p3.Y);
                double maxY = Math.Max(p1.Y, p3.Y);

                // Bottom edge
                allEdges.Add(new Segment(
                    new XYZ(minX, minY, 0),
                    new XYZ(maxX, minY, 0)));
                // Top edge
                allEdges.Add(new Segment(
                    new XYZ(minX, maxY, 0),
                    new XYZ(maxX, maxY, 0)));
                // Left edge
                allEdges.Add(new Segment(
                    new XYZ(minX, minY, 0),
                    new XYZ(minX, maxY, 0)));
                // Right edge
                allEdges.Add(new Segment(
                    new XYZ(maxX, minY, 0),
                    new XYZ(maxX, maxY, 0)));
            }

            // 2. Remove or trim edges that are shared (fully or partially) by two rectangles.
            List<Segment> boundarySegments = SubtractOverlaps(allEdges);

            // 3. Offset each boundary segment outward by 'gap'.
            List<Segment> offsetSegments = new List<Segment>();
            foreach (Segment seg in boundarySegments)
            {
                bool horizontal = Math.Abs(seg.Start.Y - seg.End.Y) < 1e-9;
                bool vertical = Math.Abs(seg.Start.X - seg.End.X) < 1e-9;
                XYZ mid = (seg.Start + seg.End) / 2.0;

                // Determine outward normal by checking which side is outside
                XYZ normal = XYZ.Zero;
                if (horizontal)
                {
                    // Guess upward
                    normal = new XYZ(0, 1, 0);
                    // If that guess is inside, reverse
                    if (IsPointInsideAnyRect(mid + normal * 0.01, placedRectangles, shiftX, shiftY))
                        normal = new XYZ(0, -1, 0);
                }
                else if (vertical)
                {
                    // Guess right
                    normal = new XYZ(1, 0, 0);
                    // If that guess is inside, reverse
                    if (IsPointInsideAnyRect(mid + normal * 0.01, placedRectangles, shiftX, shiftY))
                        normal = new XYZ(-1, 0, 0);
                }

                XYZ offStart = seg.Start + normal * gap;
                XYZ offEnd = seg.End + normal * gap;
                offsetSegments.Add(new Segment(offStart, offEnd));
            }

            // 4. Draw these offset segments in the active view, in red.
            Plane boundaryPlane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
            SketchPlane boundarySketch = SketchPlane.Create(doc, boundaryPlane);
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(255, 0, 0));

            foreach (Segment seg in offsetSegments)
            {
                Line line = Line.CreateBound(seg.Start, seg.End);
                DetailCurve dc = doc.Create.NewDetailCurve(doc.ActiveView, line);
                doc.ActiveView.SetElementOverrides(dc.Id, ogs);
            }
        }

        /// <summary>
        /// Struct for a purely horizontal or vertical segment.
        /// </summary>
        private struct Segment
        {
            public XYZ Start;
            public XYZ End;
            public Segment(XYZ s, XYZ e)
            {
                // Ensure consistent ordering
                if (Math.Abs(s.X - e.X) < 1e-9)
                {
                    // Vertical
                    if (s.Y > e.Y) { var tmp = s; s = e; e = tmp; }
                }
                else
                {
                    // Horizontal
                    if (s.X > e.X) { var tmp = s; s = e; e = tmp; }
                }
                Start = s;
                End = e;
            }
        }

        /// <summary>
        /// Removes (or trims) any segments that overlap with another segment (meaning
        /// two rectangles share that edge). Only the non-overlapping portions remain.
        /// </summary>
        private List<Segment> SubtractOverlaps(List<Segment> allEdges)
        {
            List<Segment> result = new List<Segment>(allEdges);

            for (int i = 0; i < result.Count; i++)
            {
                for (int j = i + 1; j < result.Count; j++)
                {
                    Segment s1 = result[i];
                    Segment s2 = result[j];

                    // If they're not parallel/collinear, skip
                    bool s1Horizontal = Math.Abs(s1.Start.Y - s1.End.Y) < 1e-9;
                    bool s2Horizontal = Math.Abs(s2.Start.Y - s2.End.Y) < 1e-9;
                    if (s1Horizontal != s2Horizontal)
                        continue;

                    // Check if they lie on the same line
                    if (s1Horizontal)
                    {
                        // Same Y?
                        double y1 = s1.Start.Y;
                        double y2 = s2.Start.Y;
                        if (Math.Abs(y1 - y2) > 1e-9)
                            continue;

                        // Overlapping X intervals?
                        double s1Start = s1.Start.X;
                        double s1End = s1.End.X;
                        double s2Start = s2.Start.X;
                        double s2End = s2.End.X;
                        if (s1End < s2Start || s2End < s1Start)
                            continue;

                        // Subtract overlap from both
                        result[i] = SubtractOverlap1D(s1, s2);
                        result[j] = SubtractOverlap1D(s2, s1);
                    }
                    else
                    {
                        // Vertical, same X?
                        double x1 = s1.Start.X;
                        double x2 = s2.Start.X;
                        if (Math.Abs(x1 - x2) > 1e-9)
                            continue;

                        // Overlapping Y intervals?
                        double s1Start = s1.Start.Y;
                        double s1End = s1.End.Y;
                        double s2Start = s2.Start.Y;
                        double s2End = s2.End.Y;
                        if (s1End < s2Start || s2End < s1Start)
                            continue;

                        // Subtract overlap from both
                        result[i] = SubtractOverlap1D(s1, s2);
                        result[j] = SubtractOverlap1D(s2, s1);
                    }
                }
            }

            // Remove zero-length segments
            result = result.Where(s => GetLength(s) > 1e-9).ToList();
            return result;
        }

        /// <summary>
        /// Subtracts any overlap between two collinear segments s1 and s2
        /// (horizontal or vertical). Returns the portion of s1 that remains.
        /// </summary>
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

                if (overlapEnd <= overlapStart)
                {
                    // No overlap
                    return s1;
                }
                else if (overlapStart <= s1Start && overlapEnd >= s1End)
                {
                    // s1 fully overlapped
                    return new Segment(new XYZ(s1Start, y, 0), new XYZ(s1Start, y, 0));
                }
                else if (overlapStart <= s1Start && overlapEnd < s1End)
                {
                    // Overlap from left side
                    return new Segment(
                        new XYZ(overlapEnd, y, 0),
                        new XYZ(s1End, y, 0));
                }
                else if (overlapStart > s1Start && overlapEnd >= s1End)
                {
                    // Overlap from right side
                    return new Segment(
                        new XYZ(s1Start, y, 0),
                        new XYZ(overlapStart, y, 0));
                }
                else
                {
                    // Overlap in the middle; keep just the left side
                    return new Segment(
                        new XYZ(s1Start, y, 0),
                        new XYZ(overlapStart, y, 0));
                }
            }
            else
            {
                // Vertical
                double x = s1.Start.X;
                double s1Start = s1.Start.Y;
                double s1End = s1.End.Y;
                double s2Start = s2.Start.Y;
                double s2End = s2.End.Y;

                double overlapStart = Math.Max(s1Start, s2Start);
                double overlapEnd = Math.Min(s1End, s2End);

                if (overlapEnd <= overlapStart)
                {
                    // No overlap
                    return s1;
                }
                else if (overlapStart <= s1Start && overlapEnd >= s1End)
                {
                    // Fully overlapped
                    return new Segment(new XYZ(x, s1Start, 0), new XYZ(x, s1Start, 0));
                }
                else if (overlapStart <= s1Start && overlapEnd < s1End)
                {
                    // Overlap from bottom
                    return new Segment(
                        new XYZ(x, overlapEnd, 0),
                        new XYZ(x, s1End, 0));
                }
                else if (overlapStart > s1Start && overlapEnd >= s1End)
                {
                    // Overlap from top
                    return new Segment(
                        new XYZ(x, s1Start, 0),
                        new XYZ(x, overlapStart, 0));
                }
                else
                {
                    // Overlap in the middle; keep bottom portion
                    return new Segment(
                        new XYZ(x, s1Start, 0),
                        new XYZ(x, overlapStart, 0));
                }
            }
        }

        /// <summary>
        /// Checks if the given point is inside any of the placed rectangles
        /// by bounding box check.
        /// </summary>
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

        /// <summary>
        /// Returns the length of a horizontal or vertical segment.
        /// </summary>
        private double GetLength(Segment s)
        {
            if (Math.Abs(s.Start.X - s.End.X) < 1e-9)
            {
                return Math.Abs(s.End.Y - s.Start.Y);
            }
            else
            {
                return Math.Abs(s.End.X - s.Start.X);
            }
        }

    }


}


