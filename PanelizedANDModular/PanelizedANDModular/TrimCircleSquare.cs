using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace PanelizedAndModularFinal
{
    public class TrimCircleSquare
    {
        // This property accumulates the total area that was "trimmed" off from all rooms.
        public double TotalTrimmedArea { get; private set; }

        /// <summary>
        /// Main routine that processes square rooms by:
        /// 1. Trimming each square to a boundary polygon.
        /// 2. Creating a 3D solid (extrusion) for the kept (clipped) area.
        /// 3. Displaying the trimmed-off parts as red 2D detail curves.
        /// 
        /// The method uses precomputed perimeter lines for each square.
        /// </summary>
        public void CreateTrimmedSquares(Document doc, List<XYZ[]> roomSquares, List<List<Line>> roomSquareLines, List<Line> perimeterLines)
        {
            // Reconstruct the boundary polygon from the given perimeter lines.
            List<XYZ> boundaryPolygon = BuildBoundaryPolygon(perimeterLines);
            if (boundaryPolygon.Count < 3)
            {
                TaskDialog.Show("Error", "Boundary polygon is invalid or empty.");
                return;
            }

            // Iterate over each room square.
            for (int i = 0; i < roomSquares.Count; i++)
            {
                XYZ[] square = roomSquares[i];

                // Use the precomputed area and name from saved global data.
                double originalArea = GlobalData.SavedSpaces[i].SquareArea;
                string name = GlobalData.SavedSpaces[i].Name;

                // Clip the square to the boundary polygon.
                XYZ[] clippedSquare = ClipSquareToPolygon(square, boundaryPolygon);

                // Calculate the area of the kept (clipped) portion.
                double clippedArea = CalculateArea(clippedSquare);
                // The trimmed area is what is removed from the original square.
                double trimmedArea = originalArea - clippedArea;

                // Display a message with the details for this room.
                string message = $"Room: {name}\n" +
                                 $"Original Area: {originalArea}\n" +
                                 $"Clipped Area: {clippedArea}\n" +
                                 $"Trimmed Area: {trimmedArea}";
                TaskDialog.Show("Results", message);

                // If any area is trimmed, add it to the global counter.
                if (trimmedArea > 1e-9)
                    TotalTrimmedArea += trimmedArea;

                // If the clipped (kept) area is valid, create a 3D solid (extrusion) for it.
                if (clippedArea > 1e-9)
                {
                    CreateSquareSolid(doc, clippedSquare, 1.0 /* height */);
                }
                // For the trimmed parts, display 2D red lines using the precomputed perimeter lines.
                if (trimmedArea > 1e-9)
                {
                    CreateTrimmedLines2D(doc, roomSquareLines[i], clippedSquare);
                }
            }
        }

        /// <summary>
        /// Creates red 2D detail curves in the active view for portions of the square that were trimmed off.
        /// </summary>
        private void CreateTrimmedLines2D(Document doc, List<Line> originalSquareLines, XYZ[] clippedSquare)
        {
            // Use the application's tolerance to ignore very short curves.
            double shortTol = doc.Application.ShortCurveTolerance;
            // Determine which edges of the original square are outside the clipped polygon.
            List<Curve> trimmedEdges = GetTrimmedEdgesFromLines(originalSquareLines, clippedSquare, shortTol);
            if (trimmedEdges.Count == 0)
                return;

            // For each trimmed edge, create a detail curve with a red override.
            View activeView = doc.ActiveView;
            foreach (Curve edge in trimmedEdges)
            {
                if (edge.Length < shortTol)
                    continue; // Skip curves that are too short to be significant.
                DetailCurve detailCurve = doc.Create.NewDetailCurve(activeView, edge);
                OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(255, 0, 0)); // Set the curve color to red.
                activeView.SetElementOverrides(detailCurve.Id, ogs);
            }
        }

        /// <summary>
        /// Determines which portions of each square edge are outside the clipped polygon.
        /// It checks each original edge of the square:
        /// - If both endpoints are inside the clipped polygon, no trimming is needed.
        /// - If both are outside, the whole line is trimmed.
        /// - If one endpoint is inside and the other outside, the intersection point is calculated,
        ///   and only the segment from the intersection to the outside endpoint is kept.
        /// </summary>
        private List<Curve> GetTrimmedEdgesFromLines(List<Line> originalSquareLines, XYZ[] clippedSquare, double shortTol)
        {
            List<Curve> trimmedEdges = new List<Curve>();

            // Build a closed loop from the clipped polygon points.
            List<XYZ> clipPts = new List<XYZ>(clippedSquare);
            if (clipPts.Count > 0 && !clipPts[0].IsAlmostEqualTo(clipPts[clipPts.Count - 1]))
                clipPts.Add(clipPts[0]);

            // Process each edge of the original square.
            foreach (Line line in originalSquareLines)
            {
                XYZ A = line.GetEndPoint(0);
                XYZ B = line.GetEndPoint(1);
                // Determine if endpoints are inside the clipped polygon.
                bool AInside = IsPointInsidePolygon(A, clipPts);
                bool BInside = IsPointInsidePolygon(B, clipPts);

                if (AInside && BInside)
                {
                    // If both endpoints are inside, the entire edge is inside and thus not trimmed.
                    continue;
                }
                else if (!AInside && !BInside)
                {
                    // If both endpoints are outside, the entire edge is trimmed off.
                    if (line.Length >= shortTol)
                        trimmedEdges.Add(line);
                }
                else
                {
                    // For an edge that partially lies inside, find the intersection with the polygon boundary.
                    XYZ intersection = null;
                    // Loop over each edge of the clipping polygon.
                    for (int j = 0; j < clipPts.Count - 1; j++)
                    {
                        XYZ C = clipPts[j];
                        XYZ D = clipPts[j + 1];
                        // Find where the current edge (A-B) intersects the polygon edge (C-D).
                        XYZ inter = IntersectLineSegments(A, B, C, D);
                        if (inter != null)
                        {
                            intersection = inter;
                            break;
                        }
                    }
                    if (intersection != null)
                    {
                        // Depending on which endpoint is inside, create a new edge from the intersection point to the outside endpoint.
                        if (AInside)
                        {
                            if (intersection.DistanceTo(B) >= shortTol)
                                trimmedEdges.Add(Line.CreateBound(intersection, B));
                        }
                        else // B is inside in this case.
                        {
                            if (A.DistanceTo(intersection) >= shortTol)
                                trimmedEdges.Add(Line.CreateBound(A, intersection));
                        }
                    }
                }
            }
            return trimmedEdges;
        }

        /// <summary>
        /// Reconstructs a boundary polygon from a list of lines.
        /// This is done by building an adjacency list (mapping points to lines that touch them)
        /// and then traversing the connected lines to form a closed loop.
        /// Finally, the loop is enforced to have a counterclockwise (CCW) orientation.
        /// </summary>
        public List<XYZ> BuildBoundaryPolygon(List<Line> perimeterLines)
        {
            // Create an adjacency dictionary that maps each point to its connected lines.
            Dictionary<XYZ, List<Line>> adjacency = new Dictionary<XYZ, List<Line>>(new XYZComparer());
            foreach (Line line in perimeterLines)
            {
                XYZ s = line.GetEndPoint(0);
                XYZ e = line.GetEndPoint(1);
                AddToAdjacency(adjacency, s, line);
                AddToAdjacency(adjacency, e, line);
            }

            if (perimeterLines.Count == 0)
                return new List<XYZ>();

            // Start the polygon from the first endpoint of the first line.
            XYZ current = perimeterLines[0].GetEndPoint(0);
            List<XYZ> polygon = new List<XYZ>();
            polygon.Add(current);

            Line nextLine = null;
            // Traverse the lines to build the polygon.
            while (true)
            {
                List<Line> candidateLines = adjacency[current];
                if (candidateLines.Count == 0)
                    break;

                // Choose a candidate line and remove it from the list.
                nextLine = candidateLines[0];
                candidateLines.Remove(nextLine);

                // Determine the next point on the polygon.
                XYZ s = nextLine.GetEndPoint(0);
                XYZ e = nextLine.GetEndPoint(1);
                XYZ nextPoint = s.IsAlmostEqualTo(current) ? e : s;

                // Remove the used line from the adjacent list of the next point.
                adjacency[nextPoint].Remove(nextLine);
                current = nextPoint;

                // Add the point unless we've closed the loop.
                if (!polygon[0].IsAlmostEqualTo(current))
                {
                    polygon.Add(current);
                }
                else
                {
                    // Ensure the polygon is properly closed.
                    if (!polygon[polygon.Count - 1].IsAlmostEqualTo(polygon[0]))
                        polygon.Add(polygon[0]);
                    break;
                }
            }

            // Reverse the polygon if it is not in counterclockwise order.
            if (!IsCounterClockwise(polygon))
            {
                polygon.Reverse();
            }
            return polygon;
        }

        /// <summary>
        /// Helper to add a line to the adjacency dictionary.
        /// Uses a near-equality check for the key.
        /// </summary>
        private void AddToAdjacency(Dictionary<XYZ, List<Line>> adjacency, XYZ key, Line line)
        {
            XYZ existingKey = FindMatchingKey(adjacency, key);
            if (existingKey == null)
            {
                adjacency[key] = new List<Line> { line };
            }
            else
            {
                adjacency[existingKey].Add(line);
            }
        }

        /// <summary>
        /// Determines whether the given polygon points (ordered) form a counterclockwise (CCW) polygon.
        /// It does this by computing the signed area.
        /// </summary>
        private bool IsCounterClockwise(List<XYZ> polygon)
        {
            double signedArea = 0;
            for (int i = 0; i < polygon.Count - 1; i++)
            {
                XYZ p1 = polygon[i];
                XYZ p2 = polygon[i + 1];
                signedArea += (p1.X * p2.Y - p2.X * p1.Y);
            }
            return signedArea > 0;
        }

        /// <summary>
        /// Searches for a key in the adjacency dictionary that is almost equal to the candidate point.
        /// </summary>
        private XYZ FindMatchingKey(Dictionary<XYZ, List<Line>> adjacency, XYZ candidate)
        {
            foreach (XYZ k in adjacency.Keys)
            {
                if (k.IsAlmostEqualTo(candidate))
                    return k;
            }
            return null;
        }

        /// <summary>
        /// A comparer for XYZ objects that uses the IsAlmostEqualTo method for near-equality.
        /// </summary>
        private class XYZComparer : IEqualityComparer<XYZ>
        {
            public bool Equals(XYZ a, XYZ b)
            {
                return a.IsAlmostEqualTo(b);
            }
            public int GetHashCode(XYZ obj)
            {
                // Simplification: always return 0 since we compare by tolerance.
                return 0;
            }
        }

        /// <summary>
        /// Uses the Sutherland–Hodgman algorithm to clip a square to the boundary polygon.
        /// The algorithm clips the square against each edge of the boundary, preserving the portion
        /// that lies to the left (inside) of the edge (for a CCW polygon).
        /// </summary>
        private XYZ[] ClipSquareToPolygon(XYZ[] square, List<XYZ> boundaryPolygon)
        {
            List<XYZ> poly = new List<XYZ>(square);
            // Ensure the polygon is closed.
            if (!poly[0].IsAlmostEqualTo(poly[poly.Count - 1]))
                poly.Add(poly[0]);

            // Clip against each edge of the boundary polygon.
            for (int i = 0; i < boundaryPolygon.Count - 1; i++)
            {
                XYZ p1 = boundaryPolygon[i];
                XYZ p2 = boundaryPolygon[i + 1];

                // Compute the inward-pointing normal vector for the edge.
                XYZ edgeDir = p2 - p1;
                XYZ normal = new XYZ(-edgeDir.Y, edgeDir.X, 0);
                double offset = Dot(normal, p1);

                // Clip the polygon against the line defined by (normal, offset).
                poly = ClipPolygonAgainstLine(poly, normal, offset);
                if (poly.Count < 3)
                    break;
            }

            // If the clipped polygon is not valid (less than 3 points), return an empty array.
            if (poly.Count < 3)
                return new XYZ[0];
            if (!poly[0].IsAlmostEqualTo(poly[poly.Count - 1]))
                poly.Add(poly[0]);

            return poly.ToArray();
        }

        /// <summary>
        /// Clips a polygon against a line defined by a normal and an offset.
        /// The method evaluates each edge of the polygon:
        /// - If both vertices are inside the clipping half-space, the second vertex is kept.
        /// - If an edge crosses the line, the intersection point is computed and added.
        /// </summary>
        private List<XYZ> ClipPolygonAgainstLine(List<XYZ> polygon, XYZ normal, double offset)
        {
            List<XYZ> output = new List<XYZ>();
            for (int i = 0; i < polygon.Count - 1; i++)
            {
                XYZ current = polygon[i];
                XYZ next = polygon[i + 1];
                bool currentInside = (Dot(normal, current) >= offset - 1e-9);
                bool nextInside = (Dot(normal, next) >= offset - 1e-9);

                if (currentInside && nextInside)
                {
                    // If both points are inside, keep the next point.
                    output.Add(next);
                }
                else if (currentInside && !nextInside)
                {
                    // Exiting the clipping region; add the intersection.
                    XYZ inter = Intersect(current, next, normal, offset);
                    if (inter != null)
                        output.Add(inter);
                }
                else if (!currentInside && nextInside)
                {
                    // Entering the clipping region; add the intersection and the next point.
                    XYZ inter = Intersect(current, next, normal, offset);
                    if (inter != null)
                        output.Add(inter);
                    output.Add(next);
                }
            }
            // Ensure the output polygon is closed.
            if (output.Count > 0 && !output[0].IsAlmostEqualTo(output[output.Count - 1]))
                output.Add(output[0]);
            return output;
        }

        /// <summary>
        /// Finds the intersection point along a line segment (p1-p2) with a line defined by a normal and offset.
        /// Returns null if the segment is parallel or the intersection lies outside the segment.
        /// </summary>
        private XYZ Intersect(XYZ p1, XYZ p2, XYZ normal, double offset)
        {
            XYZ dir = p2 - p1;
            double denom = Dot(normal, dir);
            if (Math.Abs(denom) < 1e-9)
                return null; // The line is nearly parallel to the clipping edge.
            double t = (offset - Dot(normal, p1)) / denom;
            if (t < -1e-9 || t > 1 + 1e-9)
                return null; // Intersection does not lie on the segment.
            return p1 + t * dir;
        }

        /// <summary>
        /// Dot product of two XYZ vectors.
        /// </summary>
        private double Dot(XYZ a, XYZ b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        /// <summary>
        /// Computes the area of a polygon defined by an array of XYZ points.
        /// Uses the standard formula (half the absolute value of the sum over edges).
        /// </summary>
        private double CalculateArea(XYZ[] corners)
        {
            if (corners == null || corners.Length < 3)
                return 0.0;
            double area = 0.0;
            for (int i = 0; i < corners.Length - 1; i++)
            {
                XYZ c1 = corners[i];
                XYZ c2 = corners[i + 1];
                area += (c1.X * c2.Y - c2.X * c1.Y);
            }
            return Math.Abs(area) * 0.5;
        }

        /// <summary>
        /// Creates a 3D solid by extruding the given clipped polygon.
        /// The extrusion is used to represent the kept (clipped) portion of the square.
        /// </summary>
        private void CreateSquareSolid(Document doc, XYZ[] rect, double height)
        {
            if (rect.Length < 3)
                return;

            double shortTol = doc.Application.ShortCurveTolerance;
            List<Curve> edges = new List<Curve>();
            // Create edges from the polygon points, skipping segments that are too short.
            for (int i = 0; i < rect.Length - 1; i++)
            {
                if (rect[i].DistanceTo(rect[i + 1]) < shortTol)
                    continue;
                Line tempLine = Line.CreateBound(rect[i], rect[i + 1]);
                if (tempLine.Length >= shortTol)
                    edges.Add(tempLine);
            }

            if (edges.Count == 0)
                return;

            // Create a closed loop from the valid edges.
            CurveLoop loop = new CurveLoop();
            foreach (Curve c in edges)
            {
                loop.Append(c);
            }

            // Extrude the loop vertically to create a solid.
            Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop }, XYZ.BasisZ, height);

            // Create a DirectShape element and assign the extruded solid to it.
            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            ds.ApplicationId = "SquareArrangementOnly";
            ds.ApplicationDataId = Guid.NewGuid().ToString();
            ds.SetShape(new List<GeometryObject> { solid });

            // Override the graphic settings so the shape is outlined in red.
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(255, 0, 0)); // red
            doc.ActiveView.SetElementOverrides(ds.Id, ogs);
        }

        /// <summary>
        /// Determines if a given point lies inside a polygon using the ray-casting algorithm.
        /// If the point lies exactly on an edge, it is considered inside.
        /// </summary>
        private bool IsPointInsidePolygon(XYZ point, List<XYZ> polygon)
        {
            int n = polygon.Count;
            bool inside = false;
            for (int i = 0; i < n; i++)
            {
                XYZ A = polygon[i];
                XYZ B = polygon[(i + 1) % n];
                // Check if the point lies directly on the line segment.
                if (IsPointOnLineSegment(point, A, B))
                    return true;
                // Ray-casting: toggle inside/outside based on the crossing of an edge.
                if (((A.Y > point.Y) != (B.Y > point.Y)) &&
                    (point.X < (B.X - A.X) * (point.Y - A.Y) / (B.Y - A.Y) + A.X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>
        /// Checks whether a point lies on the line segment between two points A and B.
        /// Uses a tolerance to account for floating-point imprecision.
        /// </summary>
        private bool IsPointOnLineSegment(XYZ point, XYZ A, XYZ B)
        {
            double tolerance = 1e-9;
            // If the point is essentially equal to an endpoint, it is considered on the segment.
            if (point.IsAlmostEqualTo(A) || point.IsAlmostEqualTo(B))
                return true;
            // Compute the cross product to determine if the point is collinear.
            double cross = Math.Abs((B.Y - A.Y) * (point.X - A.X) - (B.X - A.X) * (point.Y - A.Y));
            if (cross > tolerance)
                return false;
            // Check that the point lies between A and B.
            double dot = (point.X - A.X) * (point.X - B.X) + (point.Y - A.Y) * (point.Y - B.Y);
            return dot <= tolerance;
        }

        /// <summary>
        /// Computes the intersection point between two line segments (A-B and C-D).
        /// Returns the intersection point if it exists within both segments; otherwise, returns null.
        /// </summary>
        private XYZ IntersectLineSegments(XYZ A, XYZ B, XYZ C, XYZ D)
        {
            double denom = (B.X - A.X) * (D.Y - C.Y) - (B.Y - A.Y) * (D.X - C.X);
            if (Math.Abs(denom) < 1e-9)
                return null; // Lines are parallel or nearly so.
            double t = ((C.X - A.X) * (D.Y - C.Y) - (C.Y - A.Y) * (D.X - C.X)) / denom;
            double u = ((C.X - A.X) * (B.Y - A.Y) - (C.Y - A.Y) * (B.X - A.X)) / denom;
            // Ensure the intersection lies on both segments.
            if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
                return A + t * (B - A);
            return null;
        }
    }
}
