using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace PanelizedAndModularFinal
{
    public class TrimCircleSquare
    {
        // Tracks total area trimmed off so we can later assign it to other cells.
        public double TotalTrimmedArea { get; private set; }

        /// <summary>
        /// Creates square rooms, trims them to the boundary polygon defined by perimeterLines,
        /// creates a 3D solid for the kept (clipped) portion, and shows the trimmed portions as 2D red lines.
        /// </summary>
        public void CreateTrimmedSquares(Document doc, List<XYZ[]> roomSquares, List<Line> perimeterLines)
        {
            List<XYZ> boundaryPolygon = BuildBoundaryPolygon(perimeterLines);
            if (boundaryPolygon.Count < 3)
            {
                TaskDialog.Show("Error", "Boundary polygon is invalid or empty.");
                return;
            }

            for (int i = 0; i < roomSquares.Count; i++)
            {
                XYZ[] square = roomSquares[i];

                // Use the stored SquareArea from the corresponding SpaceNode rather than recalculating it.
                double originalArea = GlobalData.SavedSpaces[i].SquareArea;

                XYZ[] clippedSquare = ClipSquareToPolygon(square, boundaryPolygon);
                double clippedArea = CalculateArea(clippedSquare);
                double trimmedArea = originalArea - clippedArea;
                if (trimmedArea > 1e-9)
                    TotalTrimmedArea += trimmedArea;

                // Create a solid for the clipped (kept) area.
                if (clippedArea > 1e-9)
                {
                    CreateSquareSolid(doc, clippedSquare, 1.0 /* height */);
                }
                // Create 2D red detail curves for the trimmed portions.
                if (trimmedArea > 1e-9)
                {
                    CreateTrimmedLines2D(doc, square, clippedSquare);
                }
            }
        }


        public List<XYZ> BuildBoundaryPolygon(List<Line> perimeterLines)
        {
            // Build an adjacency map of each endpoint to the lines that touch it.
            Dictionary<XYZ, List<Line>> adjacency = new Dictionary<XYZ, List<Line>>(new XYZComparer());
            foreach (Line line in perimeterLines)
            {
                XYZ s = line.GetEndPoint(0);
                XYZ e = line.GetEndPoint(1);

                AddToAdjacency(adjacency, s, line);
                AddToAdjacency(adjacency, e, line);
            }

            // Pick an arbitrary starting point from the first line.
            if (perimeterLines.Count == 0) return new List<XYZ>();
            XYZ current = perimeterLines[0].GetEndPoint(0);

            // Build the loop by following connected lines.
            List<XYZ> polygon = new List<XYZ>();
            polygon.Add(current);

            Line nextLine = null;
            while (true)
            {
                List<Line> candidateLines = adjacency[current];
                if (candidateLines.Count == 0)
                    break; // No more lines to follow.

                nextLine = candidateLines[0]; // Pick one connected line.
                candidateLines.Remove(nextLine); // Mark as used.

                // Identify the next endpoint.
                XYZ s = nextLine.GetEndPoint(0);
                XYZ e = nextLine.GetEndPoint(1);
                XYZ nextPoint = s.IsAlmostEqualTo(current) ? e : s;

                // Remove this line from the other endpoint's list.
                adjacency[nextPoint].Remove(nextLine);

                // Advance to the next endpoint.
                current = nextPoint;
                if (!polygon[0].IsAlmostEqualTo(current))
                {
                    polygon.Add(current);
                }
                else
                {
                    // Ensure closure.
                    if (!polygon[polygon.Count - 1].IsAlmostEqualTo(polygon[0]))
                        polygon.Add(polygon[0]);
                    break;
                }
            }

            return polygon;
        }

        // Helper: Adds a line to adjacency, matching the key via approximate comparison.
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

        // Helper: Finds an existing key in the dictionary that is almost equal to candidate.
        private XYZ FindMatchingKey(Dictionary<XYZ, List<Line>> adjacency, XYZ candidate)
        {
            foreach (XYZ k in adjacency.Keys)
            {
                if (k.IsAlmostEqualTo(candidate))
                    return k;
            }
            return null;
        }

        // Custom IEqualityComparer<XYZ> using a small tolerance.
        private class XYZComparer : IEqualityComparer<XYZ>
        {
            public bool Equals(XYZ a, XYZ b)
            {
                return a.IsAlmostEqualTo(b);
            }
            public int GetHashCode(XYZ obj)
            {
                // Simplistic hash; could be improved.
                return 0;
            }
        }

        private XYZ[] ClipSquareToPolygon(XYZ[] square, List<XYZ> boundaryPolygon)
        {
            List<XYZ> poly = new List<XYZ>(square);
            if (!poly[0].IsAlmostEqualTo(poly[poly.Count - 1]))
                poly.Add(poly[0]);

            // Sutherland–Hodgman polygon clipping.
            for (int i = 0; i < boundaryPolygon.Count - 1; i++)
            {
                XYZ p1 = boundaryPolygon[i];
                XYZ p2 = boundaryPolygon[i + 1];

                // Compute the outward normal (assuming the boundary is in CCW order).
                XYZ edgeDir = (p2 - p1);
                XYZ normal = new XYZ(-edgeDir.Y, edgeDir.X, 0);
                double offset = Dot(normal, p1);

                poly = ClipPolygonAgainstLine(poly, normal, offset);
                if (poly.Count < 3)
                    break;
            }

            if (poly.Count < 3)
                return new XYZ[0];
            if (!poly[0].IsAlmostEqualTo(poly[poly.Count - 1]))
                poly.Add(poly[0]);
            return poly.ToArray();
        }

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
                    output.Add(next);
                }
                else if (currentInside && !nextInside)
                {
                    XYZ inter = Intersect(current, next, normal, offset);
                    if (inter != null)
                        output.Add(inter);
                }
                else if (!currentInside && nextInside)
                {
                    XYZ inter = Intersect(current, next, normal, offset);
                    if (inter != null)
                        output.Add(inter);
                    output.Add(next);
                }
            }
            if (output.Count > 0 && !output[0].IsAlmostEqualTo(output[output.Count - 1]))
                output.Add(output[0]);
            return output;
        }

        private XYZ Intersect(XYZ p1, XYZ p2, XYZ normal, double offset)
        {
            XYZ dir = p2 - p1;
            double denom = Dot(normal, dir);
            if (Math.Abs(denom) < 1e-9)
                return null; // Parallel.
            double t = (offset - Dot(normal, p1)) / denom;
            if (t < -1e-9 || t > 1 + 1e-9)
                return null; // Outside segment.
            return p1 + t * dir;
        }

        private double Dot(XYZ a, XYZ b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

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
        /// Creates a 3D solid for the clipped polygon.
        /// </summary>
        private void CreateSquareSolid(Document doc, XYZ[] rect, double height)
        {
            if (rect.Length < 3)
                return;

            double shortTol = doc.Application.ShortCurveTolerance;
            List<Curve> edges = new List<Curve>();
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

            CurveLoop loop = new CurveLoop();
            foreach (Curve c in edges)
            {
                loop.Append(c);
            }

            Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop }, XYZ.BasisZ, height);

            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            ds.ApplicationId = "SquareArrangementOnly";
            ds.ApplicationDataId = Guid.NewGuid().ToString();
            ds.SetShape(new List<GeometryObject> { solid });

            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(255, 0, 0)); // red
            doc.ActiveView.SetElementOverrides(ds.Id, ogs);
        }

        /// <summary>
        /// Creates 2D red DetailCurves for portions of the original square that were trimmed away.
        /// Assumes an active transaction is already running.
        /// </summary>
        private void CreateTrimmedLines2D(Document doc, XYZ[] originalSquare, XYZ[] clippedSquare)
        {
            double shortTol = doc.Application.ShortCurveTolerance;
            List<Curve> trimmedEdges = GetTrimmedEdges(originalSquare, clippedSquare, shortTol);
            if (trimmedEdges.Count == 0)
                return;

            View activeView = doc.ActiveView;
            foreach (Curve edge in trimmedEdges)
            {
                if (edge.Length < shortTol)
                    continue; // Skip curves that are too short.
                DetailCurve detailCurve = doc.Create.NewDetailCurve(activeView, edge);
                OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(255, 0, 0)); // red
                activeView.SetElementOverrides(detailCurve.Id, ogs);
            }
        }

        /// <summary>
        /// Returns line segments representing parts of the original square that are outside the clipped polygon.
        /// </summary>
        private List<Curve> GetTrimmedEdges(XYZ[] originalSquare, XYZ[] clippedSquare, double shortTol)
        {
            List<Curve> trimmedEdges = new List<Curve>();

            // Build closed loops for the original square and clipped polygon.
            List<XYZ> origPts = new List<XYZ>(originalSquare);
            if (!origPts[0].IsAlmostEqualTo(origPts[origPts.Count - 1]))
                origPts.Add(origPts[0]);

            List<XYZ> clipPts = new List<XYZ>(clippedSquare);
            if (clipPts.Count == 0)
                return trimmedEdges;
            if (!clipPts[0].IsAlmostEqualTo(clipPts[clipPts.Count - 1]))
                clipPts.Add(clipPts[0]);

            // Evaluate each edge of the original square.
            for (int i = 0; i < origPts.Count - 1; i++)
            {
                XYZ A = origPts[i];
                XYZ B = origPts[i + 1];
                bool AInside = IsPointInsidePolygon(A, clipPts);
                bool BInside = IsPointInsidePolygon(B, clipPts);

                if (AInside && BInside)
                {
                    continue;
                }
                else if (!AInside && !BInside)
                {
                    if (A.DistanceTo(B) < shortTol)
                        continue;
                    Line edgeLine = Line.CreateBound(A, B);
                    if (edgeLine.Length >= shortTol)
                        trimmedEdges.Add(edgeLine);
                }
                else
                {
                    XYZ intersection = null;
                    for (int j = 0; j < clipPts.Count - 1; j++)
                    {
                        XYZ C = clipPts[j];
                        XYZ D = clipPts[j + 1];
                        XYZ inter = IntersectLineSegments(A, B, C, D);
                        if (inter != null)
                        {
                            intersection = inter;
                            break;
                        }
                    }
                    if (intersection != null)
                    {
                        Line trimmed;
                        if (AInside)
                        {
                            if (intersection.DistanceTo(B) < shortTol)
                                continue;
                            trimmed = Line.CreateBound(intersection, B);
                        }
                        else
                        {
                            if (A.DistanceTo(intersection) < shortTol)
                                continue;
                            trimmed = Line.CreateBound(A, intersection);
                        }
                        if (trimmed.Length >= shortTol)
                            trimmedEdges.Add(trimmed);
                    }
                }
            }
            return trimmedEdges;
        }

        /// <summary>
        /// Determines if a point is inside a closed polygon (in the XY plane) using the ray-casting method.
        /// </summary>
        private bool IsPointInsidePolygon(XYZ point, List<XYZ> polygon)
        {
            bool inside = false;
            for (int i = 0; i < polygon.Count - 1; i++)
            {
                XYZ A = polygon[i];
                XYZ B = polygon[i + 1];
                if (((A.Y > point.Y) != (B.Y > point.Y)) &&
                    (point.X < (B.X - A.X) * (point.Y - A.Y) / (B.Y - A.Y) + A.X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>
        /// Computes the intersection point between two line segments, if one exists.
        /// </summary>
        private XYZ IntersectLineSegments(XYZ A, XYZ B, XYZ C, XYZ D)
        {
            double denom = (B.X - A.X) * (D.Y - C.Y) - (B.Y - A.Y) * (D.X - C.X);
            if (Math.Abs(denom) < 1e-9)
                return null;
            double t = ((C.X - A.X) * (D.Y - C.Y) - (C.Y - A.Y) * (D.X - C.X)) / denom;
            double u = ((C.X - A.X) * (B.Y - A.Y) - (C.Y - A.Y) * (B.X - A.X)) / denom;
            if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
                return A + t * (B - A);
            return null;
        }
    }
}