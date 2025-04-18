using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

public class SquareBoundaryTrimmer
{
    /// <summary>
    /// Main method that trims each square to the boundary and updates its trimmed area.
    /// </summary>
    public void TrimSquaresToBoundary(
        List<Line> boundaryLines,
        List<XYZ[]> squares,
        List<MySquareData> squareData)
    {
        // 1) Build the boundary polygon in 2D.
        List<XYZ> boundaryPolygon3D = BuildBoundaryPolygon(boundaryLines);
        List<Point2D> boundaryPolygon2D = To2D(boundaryPolygon3D);


   

        // 2) For each square, clip and compute area.
        for (int i = 0; i < squares.Count; i++)
        {
            XYZ[] sqPts3D = squares[i];
            List<Point2D> squarePolygon2D = To2D(new List<XYZ>(sqPts3D));

            // Clip using Sutherland-Hodgman.
            List<Point2D> clippedPolygon2D = SutherlandHodgmanClip(squarePolygon2D, boundaryPolygon2D);

            double clippedArea = PolygonArea(clippedPolygon2D);
            squareData[i].SquareTrimmedArea = clippedArea; // store new trimmed area

            // 3) Build lines for the clipped shape (to visualize in Revit).
            List<XYZ> clippedPolygon3D = From2D(clippedPolygon2D, sqPts3D[0].Z);
            List<Line> clippedLines = BuildPolygonLines(clippedPolygon3D);
            squareData[i].TrimmedLines = clippedLines; // store the trimmed shape lines
        }
    }

    /// <summary>
    /// Draws the trimmed lines as red detail curves in the active view.
    /// </summary>
    public void DrawTrimmedLines(Document doc, List<MySquareData> squareData)
    {
        // Create override settings with red line color.
        OverrideGraphicSettings ogs = new OverrideGraphicSettings();
        ogs.SetProjectionLineColor(new Color(255, 0, 0));

        // Start a transaction to add detail curves.
        using (Transaction t = new Transaction(doc, "Draw Trimmed Lines"))
        {
            t.Start();
            foreach (MySquareData data in squareData)
            {
                if (data.TrimmedLines != null)
                {
                    foreach (Line line in data.TrimmedLines)
                    {
                        // Create a detail curve in the active view.
                        DetailCurve detailCurve = doc.Create.NewDetailCurve(doc.ActiveView, line);
                        // Override its graphic settings to display red.
                        doc.ActiveView.SetElementOverrides(detailCurve.Id, ogs);
                    }
                }
            }
            t.Commit();
        }
    }

    /// <summary>
    /// Example container for original square data.
    /// </summary>
    public class MySquareData
    {
        public double SquareArea;       // original area
        public double SquareTrimmedArea; // area after clipping
        public List<Line> TrimmedLines;  // lines of clipped shape
    }

    // -----------------------------
    // Polygon building from lines (provided for completeness)
    // -----------------------------
    public List<XYZ> BuildBoundaryPolygon(List<Line> perimeterLines)
    {
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

        XYZ current = perimeterLines[0].GetEndPoint(0);
        List<XYZ> polygon = new List<XYZ>();
        polygon.Add(current);

        Line nextLine = null;
        while (true)
        {
            List<Line> candidateLines = adjacency[current];
            if (candidateLines.Count == 0)
                break;

            nextLine = candidateLines[0];
            candidateLines.Remove(nextLine);

            XYZ s = nextLine.GetEndPoint(0);
            XYZ e = nextLine.GetEndPoint(1);
            XYZ nextPoint = s.IsAlmostEqualTo(current) ? e : s;

            adjacency[nextPoint].Remove(nextLine);
            current = nextPoint;

            if (!polygon[0].IsAlmostEqualTo(current))
            {
                polygon.Add(current);
            }
            else
            {
                if (!polygon[polygon.Count - 1].IsAlmostEqualTo(polygon[0]))
                    polygon.Add(polygon[0]);
                break;
            }
        }

        if (!IsCounterClockwise(polygon))
        {
            polygon.Reverse();
        }
        return polygon;
    }

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

    private XYZ FindMatchingKey(Dictionary<XYZ, List<Line>> adjacency, XYZ candidate)
    {
        foreach (XYZ k in adjacency.Keys)
        {
            if (k.IsAlmostEqualTo(candidate))
                return k;
        }
        return null;
    }

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

    // -----------------------------
    // Conversion between XYZ and 2D points
    // -----------------------------
    private List<Point2D> To2D(List<XYZ> points3D)
    {
        var result = new List<Point2D>();
        foreach (var p in points3D)
            result.Add(new Point2D(p.X, p.Y));
        return result;
    }
    private List<XYZ> From2D(List<Point2D> points2D, double z)
    {
        var result = new List<XYZ>();
        foreach (var p in points2D)
            result.Add(new XYZ(p.X, p.Y, z));
        return result;
    }

    // -----------------------------
    // Sutherland–Hodgman Polygon Clipping
    // -----------------------------
    private List<Point2D> SutherlandHodgmanClip(List<Point2D> subject, List<Point2D> clip)
    {
        List<Point2D> output = new List<Point2D>(subject);
        for (int i = 0; i < clip.Count; i++)
        {
            List<Point2D> input = output;
            output = new List<Point2D>();

            // Current clip edge: from clip[i] to clip[i+1]
            Point2D A = clip[i];
            Point2D B = clip[(i + 1) % clip.Count];

            for (int j = 0; j < input.Count; j++)
            {
                Point2D P = input[j];
                Point2D Q = input[(j + 1) % input.Count];

                bool insideP = IsInside(P, A, B);
                bool insideQ = IsInside(Q, A, B);

                if (insideP && insideQ)
                {
                    // Both in: keep Q.
                    output.Add(Q);
                }
                else if (insideP && !insideQ)
                {
                    // P in, Q out: add intersection.
                    output.Add(Intersect(P, Q, A, B));
                }
                else if (!insideP && insideQ)
                {
                    // P out, Q in: intersection + Q.
                    output.Add(Intersect(P, Q, A, B));
                    output.Add(Q);
                }
            }
        }
        return output;
    }

    private bool IsInside(Point2D test, Point2D A, Point2D B)
    {
        // "Left" test for CCW clip boundary.
        return (B.X - A.X) * (test.Y - A.Y) - (B.Y - A.Y) * (test.X - A.X) >= 0;
    }

    private Point2D Intersect(Point2D P, Point2D Q, Point2D A, Point2D B)
    {
        double A1 = Q.Y - P.Y;
        double B1 = P.X - Q.X;
        double C1 = A1 * P.X + B1 * P.Y;

        double A2 = B.Y - A.Y;
        double B2 = A.X - B.X;
        double C2 = A2 * A.X + B2 * A.Y;

        double det = A1 * B2 - A2 * B1;
        if (Math.Abs(det) < 1e-9)
            return P; // Lines are parallel or very close.

        double x = (B2 * C1 - B1 * C2) / det;
        double y = (A1 * C2 - A2 * C1) / det;
        return new Point2D(x, y);
    }

    // -----------------------------
    // Compute area of a 2D polygon
    // -----------------------------
    private double PolygonArea(List<Point2D> poly)
    {
        double area = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            Point2D p1 = poly[i];
            Point2D p2 = poly[(i + 1) % poly.Count];
            area += (p1.X * p2.Y - p2.X * p1.Y);
        }
        return Math.Abs(area) / 2.0;
    }

    // -----------------------------
    // Convert clipped polygon to Revit Lines
    // -----------------------------
    private List<Line> BuildPolygonLines(List<XYZ> poly3D)
    {
        var lines = new List<Line>();
        for (int i = 0; i < poly3D.Count - 1; i++)
        {
            lines.Add(Line.CreateBound(poly3D[i], poly3D[i + 1]));
        }
        return lines;
    }

    /// <summary>
    /// Simple 2D point struct.
    /// </summary>
    private struct Point2D
    {
        public double X, Y;
        public Point2D(double x, double y) { X = x; Y = y; }
    }

    public class XYZComparer : IEqualityComparer<XYZ>
    {
        private const double Tolerance = 1e-9;
        public bool Equals(XYZ a, XYZ b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return a.IsAlmostEqualTo(b);
        }
        public int GetHashCode(XYZ obj)
        {
            if (obj is null)
                return 0;
            int hashX = Math.Round(obj.X / Tolerance).GetHashCode();
            int hashY = Math.Round(obj.Y / Tolerance).GetHashCode();
            int hashZ = Math.Round(obj.Z / Tolerance).GetHashCode();
            return hashX ^ hashY ^ hashZ;
        }
    }
}
