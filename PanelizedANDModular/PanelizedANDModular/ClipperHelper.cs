using Autodesk.Revit.DB;
using ClipperLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

// Helper class for conversion between Revit and Clipper
public static class ClipperHelper
{
    // Scale factor to convert between Revit's double coordinates and Clipper's integer coordinates.
    private const double scale = 1e6;

    public static List<IntPoint> ToClipperPath(CurveLoop loop)
    {
        List<IntPoint> path = new List<IntPoint>();
        // Sample each curve's start point.
        foreach (Curve curve in loop)
        {
            XYZ pt = curve.GetEndPoint(0);
            path.Add(new IntPoint(pt.X * scale, pt.Y * scale));
        }
        // Ensure the path is closed.
        if (path.Count > 0 && (path[0].X != path[path.Count - 1].X || path[0].Y != path[path.Count - 1].Y))
            path.Add(path[0]);
        return path;
    }

    public static CurveLoop FromClipperPath(List<IntPoint> path)
    {
        CurveLoop loop = new CurveLoop();
        // Remove duplicate closing point if present.
        if (path.Count > 1 && path[0].Equals(path[path.Count - 1]))
            path.RemoveAt(path.Count - 1);

        for (int i = 0; i < path.Count; i++)
        {
            IntPoint ipCurrent = path[i];
            IntPoint ipNext = path[(i + 1) % path.Count];
            XYZ current = new XYZ(ipCurrent.X / scale, ipCurrent.Y / scale, 0);
            XYZ next = new XYZ(ipNext.X / scale, ipNext.Y / scale, 0);
            loop.Append(Line.CreateBound(current, next));
        }
        return loop;
    }
    // Method to compute intersection of two CurveLoops using Clipper
    public static List<CurveLoop> GetIntersection(CurveLoop polygonLoop, CurveLoop squareLoop)
    {
        List<IntPoint> subject = ClipperHelper.ToClipperPath(polygonLoop);
        List<IntPoint> clip = ClipperHelper.ToClipperPath(squareLoop);

        Clipper clipper = new Clipper();
        clipper.AddPath(subject, PolyType.ptSubject, true);
        clipper.AddPath(clip, PolyType.ptClip, true);

        List<List<IntPoint>> solution = new List<List<IntPoint>>();
        clipper.Execute(ClipType.ctIntersection, solution, PolyFillType.pftNonZero, PolyFillType.pftNonZero);

        List<CurveLoop> loops = new List<CurveLoop>();
        foreach (var sol in solution)
        {
            try
            {
                CurveLoop loop = ClipperHelper.FromClipperPath(sol);
                loops.Add(loop);
            }
            catch (Exception ex)
            {
                // Handle any conversion exceptions if needed.
                throw new Exception("Error converting Clipper path to CurveLoop", ex);
            }
        }
        return loops;
    }

}

