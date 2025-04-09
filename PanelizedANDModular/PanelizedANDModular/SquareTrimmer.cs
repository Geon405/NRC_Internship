using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

public class SquareTrimmer
{
    private readonly List<XYZ> _boundaryPolygon;

    public SquareTrimmer(List<Line> boundaryLines)
    {
        // Convert boundary lines into an ordered polygon (ensure CCW).
        _boundaryPolygon = ConvertLinesToPolygonPoints(boundaryLines);
    }

    public SquareTrimmer()
    {
   
    }

    /// <summary>
    /// Clips the given square (4 points) against the boundary polygon and
    /// returns the trimmed polygon as lines, plus how much area was trimmed off.
    /// </summary>
    public List<Line> TrimSquare(
        XYZ[] squarePoints,
        double originalArea,
        out double trimmedOffArea)
    {
        // 1. Order square points (clockwise or CCW, but consistently)
        List<XYZ> orderedSquare = OrderPointsClockwise(squarePoints);

        // 2. Clip the square polygon against the boundary polygon
        List<XYZ> clippedPoints = ClipPolygon(orderedSquare, _boundaryPolygon);

        // 3. Compute clipped area
        double clippedArea = Math.Abs(ComputePolygonArea(clippedPoints));

        // 4. Calculate trimmed area
        trimmedOffArea = originalArea - clippedArea;
        if (trimmedOffArea < 0) trimmedOffArea = 0;

        // 5. Convert clipped polygon points back to lines for visualization
        return PolygonPointsToLines(clippedPoints);
    }

    private List<XYZ> OrderPointsClockwise(XYZ[] points)
    {
        // Compute the centroid
        double centerX = points.Average(p => p.X);
        double centerY = points.Average(p => p.Y);
        XYZ center = new XYZ(centerX, centerY, 0);

        // Sort points by angle relative to the center
        return points
            .OrderBy(p => Math.Atan2(p.Y - centerY, p.X - centerX))
            .ToList();
    }

    // --------------------------------------------------
    // Core clipping (Sutherland–Hodgman)
    // --------------------------------------------------
    private List<XYZ> ClipPolygon(List<XYZ> subject, List<XYZ> clip)
    {
        List<XYZ> outputList = subject;
        for (int i = 0; i < clip.Count; i++)
        {
            List<XYZ> inputList = outputList.ToList();
            outputList.Clear();

            XYZ A = clip[i];
            XYZ B = clip[(i + 1) % clip.Count];

            for (int j = 0; j < inputList.Count; j++)
            {
                XYZ P = inputList[j];
                XYZ Q = inputList[(j + 1) % inputList.Count];

                bool insideP = IsInside(P, A, B);
                bool insideQ = IsInside(Q, A, B);

                if (insideP && insideQ)
                {
                    // Both points inside
                    outputList.Add(Q);
                }
                else if (insideP && !insideQ)
                {
                    // P inside, Q outside
                    outputList.Add(Intersect(P, Q, A, B));
                }
                else if (!insideP && insideQ)
                {
                    // P outside, Q inside
                    outputList.Add(Intersect(P, Q, A, B));
                    outputList.Add(Q);
                }
                // else both outside: add nothing
            }
        }
        return outputList;
    }

    // "Left side" = inside for CCW polygons
    private bool IsInside(XYZ pt, XYZ A, XYZ B)
    {
        // If cross > 0, pt is left of AB => inside.
        // Use a small tolerance for floating-point issues.
        return Cross(B - A, pt - A).Z >= -1e-9;
    }

    // Find intersection of segment PQ with clip edge AB
    private XYZ Intersect(XYZ P, XYZ Q, XYZ A, XYZ B)
    {
        XYZ dPQ = Q - P;
        XYZ dAB = B - A;

        double denom = Cross(dPQ, dAB).Z;
        if (Math.Abs(denom) < 1e-9) return P; // Parallel or nearly so

        double t = Cross(A - P, dAB).Z / denom;
        return P + t * dPQ;
    }

    // Cross product in XY plane (store in Z)
    private XYZ Cross(XYZ a, XYZ b)
    {
        return new XYZ(
            0,
            0,
            a.X * b.Y - a.Y * b.X
        );
    }

    // --------------------------------------------------
    // Utility methods
    // --------------------------------------------------
    public List<XYZ> ConvertLinesToPolygonPoints(List<Line> boundaryLines)
    {
        // Build a map from start -> end
        Dictionary<XYZ, XYZ> nextMap = new Dictionary<XYZ, XYZ>(new XYZEqualityComparer());
        foreach (Line ln in boundaryLines)
        {
            XYZ start = ln.GetEndPoint(0);
            XYZ end = ln.GetEndPoint(1);
            nextMap[start] = end;
        }

        // Build the polygon
        List<XYZ> polygon = new List<XYZ>();
        XYZ current = boundaryLines[0].GetEndPoint(0);
        polygon.Add(current);

        while (true)
        {
            if (!nextMap.ContainsKey(current)) break;
            XYZ nxt = nextMap[current];
            if (nxt.IsAlmostEqualTo(polygon[0])) // closed
                break;
            polygon.Add(nxt);
            current = nxt;
        }

        // Ensure the boundary is oriented CCW
        double area = ComputePolygonArea(polygon);
        if (area < 0)
        {
            polygon.Reverse(); // Now it's CCW
        }

        return polygon;
    }

    private List<Line> PolygonPointsToLines(List<XYZ> points)
    {
        List<Line> lines = new List<Line>();
        for (int i = 0; i < points.Count; i++)
        {
            XYZ start = points[i];
            XYZ end = points[(i + 1) % points.Count];
            if (!start.IsAlmostEqualTo(end))
            {
                lines.Add(Line.CreateBound(start, end));
            }
        }
        return lines;
    }

    // Shoelace formula for polygon area in XY plane
    private double ComputePolygonArea(List<XYZ> points)
    {
        if (points.Count < 3) return 0;
        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            XYZ a = points[i];
            XYZ b = points[(i + 1) % points.Count];
            area += (a.X * b.Y - b.X * a.Y);
        }
        return 0.5 * area;
    }

    // Simple XYZ comparer for dictionary usage
    private class XYZEqualityComparer : IEqualityComparer<XYZ>
    {
        public bool Equals(XYZ x, XYZ y)
        {
            return x.IsAlmostEqualTo(y);
        }

        public int GetHashCode(XYZ obj)
        {
            // Use rounded coordinates as a quick hash
            int hx = (int)Math.Round(obj.X * 10000);
            int hy = (int)Math.Round(obj.Y * 10000);
            int hz = (int)Math.Round(obj.Z * 10000);
            return hx ^ hy ^ hz;
        }
    }

    /// <summary>
    /// Creates DetailCurves from the given trimmed lines and applies
    /// override settings to draw them in red in the active view.
    /// </summary>
    public List<DetailCurve> GetVisualizationDetailCurves(Document doc, List<Line> trimmedLines)
    {
        List<DetailCurve> redDetailCurves = new List<DetailCurve>();
        foreach (Line line in trimmedLines)
        {
            DetailCurve detailCurve = doc.Create.NewDetailCurve(doc.ActiveView, line);
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(255, 0, 0));
            doc.ActiveView.SetElementOverrides(detailCurve.Id, ogs);
            redDetailCurves.Add(detailCurve);
        }
        return redDetailCurves;
    }
}
