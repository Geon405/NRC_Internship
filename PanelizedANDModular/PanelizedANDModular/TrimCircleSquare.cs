
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
        /// Creates square rooms, trims them to the grid boundary, and stores the trimmed area.
        /// </summary>
        /// <param name="doc">Revit Document</param>
        /// <param name="roomSquares">List of squares (each square is 4 XYZ corners).</param>
        /// <param name="minX">Grid boundary min X</param>
        /// <param name="minY">Grid boundary min Y</param>
        /// <param name="maxX">Grid boundary max X</param>
        /// <param name="maxY">Grid boundary max Y</param>
        public void CreateTrimmedSquares(Document doc, List<XYZ[]> roomSquares,
double minX, double minY, double maxX, double maxY)
        {
            // No transaction here; assume caller has an open transaction.
            foreach (XYZ[] square in roomSquares)
            {
                double originalArea = CalculateArea(square);

                XYZ[] clippedSquare = ClipSquareToBoundary(square, minX, minY, maxX, maxY);
                double clippedArea = CalculateArea(clippedSquare);

                double trimmedArea = originalArea - clippedArea;
                TotalTrimmedArea += Math.Max(trimmedArea, 0);

                if (clippedArea > 1e-9)
                {
                    CreateSquareSolid(doc, clippedSquare, 1.0 /* height */);
                }
            }
        }


        /// <summary>
        /// Clips a 4-corner square to the bounding region defined by minX, minY, maxX, maxY.
        /// Returns the portion of the square still inside the boundary (may be identical if fully inside).
        /// </summary>
        private XYZ[] ClipSquareToBoundary(XYZ[] square, double minX, double minY, double maxX, double maxY)
        {
            // Convert the 4-corner array into a closed polygon list (5 points: last repeats first).
            List<XYZ> polygon = new List<XYZ>(square);
            polygon.Add(polygon[0]);

            // Clip the polygon against each boundary edge in turn (left, right, bottom, top).
            polygon = ClipPolygonAgainstLine(polygon, new XYZ(1, 0, 0), minX);    // left   (x >= minX)
            polygon = ClipPolygonAgainstLine(polygon, new XYZ(-1, 0, 0), -maxX);  // right  (x <= maxX)
            polygon = ClipPolygonAgainstLine(polygon, new XYZ(0, 1, 0), minY);    // bottom (y >= minY)
            polygon = ClipPolygonAgainstLine(polygon, new XYZ(0, -1, 0), -maxY);  // top    (y <= maxY)

            // If we lost too many corners, it’s entirely outside.
            if (polygon.Count < 3)
                return new XYZ[0];

            // Ensure the polygon is closed (start == end).
            if (!polygon[0].IsAlmostEqualTo(polygon[polygon.Count - 1]))
                polygon.Add(polygon[0]);

            // Return as an array for convenience.
            return polygon.ToArray();
        }

        /// <summary>
        /// Clips a polygon against a single boundary line (normal . point >= offset).
        /// Uses a standard Sutherland–Hodgman approach.
        /// </summary>
        private List<XYZ> ClipPolygonAgainstLine(List<XYZ> polygon, XYZ normal, double offset)
        {
            List<XYZ> output = new List<XYZ>();
            for (int i = 0; i < polygon.Count - 1; i++)
            {
                XYZ current = polygon[i];
                XYZ next = polygon[i + 1];
                bool currentInside = IsInside(current, normal, offset);
                bool nextInside = IsInside(next, normal, offset);

                if (currentInside && nextInside)
                {
                    // Both endpoints inside => keep next
                    output.Add(next);
                }
                else if (currentInside && !nextInside)
                {
                    // Going from inside to outside => keep intersection
                    XYZ inter = Intersect(current, next, normal, offset);
                    if (inter != null) output.Add(inter);
                }
                else if (!currentInside && nextInside)
                {
                    // Going from outside to inside => keep intersection + next
                    XYZ inter = Intersect(current, next, normal, offset);
                    if (inter != null) output.Add(inter);
                    output.Add(next);
                }
                // Else both outside => keep nothing
            }

            // Close the polygon if it’s not empty.
            if (output.Count > 0 && !output[0].IsAlmostEqualTo(output[output.Count - 1]))
                output.Add(output[0]);

            return output;
        }

        private bool IsInside(XYZ pt, XYZ normal, double offset)
        {
            // A point is inside if dot(normal, pt) >= offset
            double dot = pt.X * normal.X + pt.Y * normal.Y + pt.Z * normal.Z;
            return dot >= offset - 1e-9;
        }

        /// <summary>
        /// Finds the intersection of segment p1->p2 with the line defined by normal . p = offset.
        /// </summary>
        private XYZ Intersect(XYZ p1, XYZ p2, XYZ normal, double offset)
        {
            XYZ dir = p2 - p1;
            double denom = normal.X * dir.X + normal.Y * dir.Y + normal.Z * dir.Z;
            if (Math.Abs(denom) < 1e-9)
                return null; // segment is parallel => no unique intersection

            double t = (offset - (normal.X * p1.X + normal.Y * p1.Y + normal.Z * p1.Z)) / denom;
            if (t < -1e-9 || t > 1 + 1e-9)
                return null; // intersection is outside the segment

            return p1 + t * dir;
        }

        private double CalculateArea(XYZ[] corners)
        {
            if (corners == null || corners.Length < 3) return 0.0;

            double area = 0.0;
            // corners is assumed closed (first == last).
            for (int i = 0; i < corners.Length - 1; i++)
            {
                XYZ c1 = corners[i];
                XYZ c2 = corners[i + 1];
                area += (c1.X * c2.Y - c2.X * c1.Y);
            }
            return Math.Abs(area) * 0.5;
        }

















        /// <summary>
        /// Creates an extrusion solid in Revit for the given clipped square.
        /// </summary>
        private void CreateSquareSolid(Document doc, XYZ[] rect, double height)
        {
            // Build boundary curves
            List<Curve> edges = new List<Curve>
    {
        Line.CreateBound(rect[0], rect[1]),
        Line.CreateBound(rect[1], rect[2]),
        Line.CreateBound(rect[2], rect[3]),
        Line.CreateBound(rect[3], rect[0])
    };

            CurveLoop loop = new CurveLoop();
            foreach (Curve c in edges)
                loop.Append(c);

            Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop }, XYZ.BasisZ, height);

            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            ds.ApplicationId = "SquareArrangementOnly";
            ds.ApplicationDataId = Guid.NewGuid().ToString();
            ds.SetShape(new List<GeometryObject> { solid });

            // Set view overrides directly, within the existing transaction.
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(255, 0, 0));//red
            doc.ActiveView.SetElementOverrides(ds.Id, ogs);
        }


    }
}


