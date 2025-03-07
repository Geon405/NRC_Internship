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
        // Property to store the red boundary's ElementIds.
        public List<ElementId> SavedBoundaryElementIds { get; private set; }

        /// <summary>
        /// Arranges modules in a square-like footprint (alternating horizontal & vertical rows)
        /// and creates each module as a rectangular solid.
        /// </summary>
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
                double dimX1 = mod.Length;
                double dimY1 = mod.Width;
                double dimX2 = mod.Width;
                double dimY2 = mod.Length;

                bool placed = false;
                double chosenX = 0, chosenY = 0;

                // Local helper to check fit.
                bool FitsInRow(double testX, double testY)
                {
                    if (offsetX + testX > landWidth) return false;
                    if (currentRowHeight > 0 && Math.Abs(testY - currentRowHeight) > 1e-9) return false;
                    if (offsetY + testY > landHeight) return false;
                    return true;
                }

                // Check default orientation.
                if (!placed && FitsInRow(dimX1, dimY1))
                {
                    chosenX = dimX1;
                    chosenY = dimY1;
                    placed = true;
                }
                // Check rotated orientation.
                if (!placed && FitsInRow(dimX2, dimY2))
                {
                    chosenX = dimX2;
                    chosenY = dimY2;
                    placed = true;
                }

                // If neither fits, start a new row.
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
                        throw new Exception("Module doesn't fit in the new row; land area too small.");
                    }
                }

                // Place the module.
                XYZ p1 = new XYZ(offsetX, offsetY, 0);
                XYZ p2 = new XYZ(offsetX + chosenX, offsetY, 0);
                XYZ p3 = new XYZ(offsetX + chosenX, offsetY + chosenY, 0);
                XYZ p4 = new XYZ(offsetX, offsetY + chosenY, 0);
                placedRectangles.Add(new XYZ[] { p1, p2, p3, p4 });

                offsetX += chosenX;
                if (Math.Abs(currentRowHeight) < 1e-9)
                    currentRowHeight = chosenY;
            }

            // 4. Build geometry in a Revit transaction.
            using (Transaction trans = new Transaction(doc, "Create Module Arrangement"))
            {
                trans.Start();

                foreach (var rect in placedRectangles)
                {
                    CreateModuleSolid(doc, rect[0], rect[1], rect[2], rect[3], 1.0);
                }

                // Create the red boundary and save its ElementIds.
                double gap = 1;
                SavedBoundaryElementIds = CreateOrthogonalBoundary(doc, placedRectangles, gap, 0, 0);

                trans.Commit();
            }
        }

        /// <summary>
        /// Creates a rectangular extrusion (DirectShape) to represent the module in 3D.
        /// 'height' is the extrusion distance.
        /// </summary>
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
        /// Shared edges are omitted to avoid diagonals.
        /// Returns a list of ElementIds for the created detail curves.
        /// </summary>
        private List<ElementId> CreateOrthogonalBoundary(
            Document doc,
            List<XYZ[]> placedRectangles,
            double gap,
            double shiftX,
            double shiftY)
        {
            List<Segment> allEdges = new List<Segment>();
            foreach (var rect in placedRectangles)
            {
                XYZ p1 = rect[0].Add(new XYZ(shiftX, shiftY, 0)); // bottom-left
                XYZ p2 = rect[1].Add(new XYZ(shiftX, shiftY, 0)); // bottom-right
                XYZ p3 = rect[2].Add(new XYZ(shiftX, shiftY, 0)); // top-right
                XYZ p4 = rect[3].Add(new XYZ(shiftX, shiftY, 0)); // top-left

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

            foreach (Segment seg in boundarySegments)
            {
                bool horizontal = Math.Abs(seg.Start.Y - seg.End.Y) < 1e-9;
                XYZ mid = (seg.Start + seg.End) / 2.0;
                XYZ normal = XYZ.Zero;

                if (horizontal)
                {
                    normal = new XYZ(0, 1, 0);
                    if (IsPointInsideAnyRect(mid + normal * 0.01, placedRectangles, shiftX, shiftY))
                        normal = new XYZ(0, -1, 0);
                }
                else
                {
                    normal = new XYZ(1, 0, 0);
                    if (IsPointInsideAnyRect(mid + normal * 0.01, placedRectangles, shiftX, shiftY))
                        normal = new XYZ(-1, 0, 0);
                }

                XYZ offStart = seg.Start + normal * gap;
                XYZ offEnd = seg.End + normal * gap;
                offsetSegments.Add(new Segment(offStart, offEnd));
            }

            Plane boundaryPlane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
            SketchPlane boundarySketch = SketchPlane.Create(doc, boundaryPlane);
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(255, 0, 0));

            List<ElementId> boundaryDetailCurveIds = new List<ElementId>();
            foreach (Segment seg in offsetSegments)
            {
                Line line = Line.CreateBound(seg.Start, seg.End);
                DetailCurve dc = doc.Create.NewDetailCurve(doc.ActiveView, line);
                doc.ActiveView.SetElementOverrides(dc.Id, ogs);
                boundaryDetailCurveIds.Add(dc.Id);
            }
            return boundaryDetailCurveIds;
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

        /// <summary>
        /// Removes (or trims) segments that overlap (shared edges between rectangles).
        /// Only non-overlapping portions remain.
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

                    bool s1Horizontal = Math.Abs(s1.Start.Y - s1.End.Y) < 1e-9;
                    bool s2Horizontal = Math.Abs(s2.Start.Y - s2.End.Y) < 1e-9;
                    if (s1Horizontal != s2Horizontal)
                        continue;

                    if (s1Horizontal)
                    {
                        if (Math.Abs(s1.Start.Y - s2.Start.Y) > 1e-9)
                            continue;

                        double s1Start = s1.Start.X;
                        double s1End = s1.End.X;
                        double s2Start = s2.Start.X;
                        double s2End = s2.End.X;
                        if (s1End < s2Start || s2End < s1Start)
                            continue;

                        result[i] = SubtractOverlap1D(s1, s2);
                        result[j] = SubtractOverlap1D(s2, s1);
                    }
                    else
                    {
                        if (Math.Abs(s1.Start.X - s2.Start.X) > 1e-9)
                            continue;

                        double s1Start = s1.Start.Y;
                        double s1End = s1.End.Y;
                        double s2Start = s2.Start.Y;
                        double s2End = s2.End.Y;
                        if (s1End < s2Start || s2End < s1Start)
                            continue;

                        result[i] = SubtractOverlap1D(s1, s2);
                        result[j] = SubtractOverlap1D(s2, s1);
                    }
                }
            }
            return result.Where(s => GetLength(s) > 1e-9).ToList();
        }

        /// <summary>
        /// Subtracts any overlap between two collinear segments (horizontal or vertical).
        /// Returns the remaining portion of the first segment.
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
                    return s1;
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

                if (overlapEnd <= overlapStart)
                    return s1;
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

        /// <summary>
        /// Checks if the given point is inside any of the placed rectangles.
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
            return Math.Abs(s.Start.X - s.End.X) < 1e-9
                ? Math.Abs(s.End.Y - s.Start.Y)
                : Math.Abs(s.End.X - s.Start.X);
        }
    }
}
