using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

public class SquareTrimmer2
{
    private Document _doc;
    private CurveLoop _polygonLoop;

    public SquareTrimmer2(Document doc, CurveLoop polygonLoop)
    {
        _doc = doc;
        _polygonLoop = polygonLoop;
    }

    public void TrimSquaresAndDisplay(List<XYZ[]> roomSquares)
    {
        using (Transaction tx = new Transaction(_doc, "Trim Squares"))
        {
            tx.Start();
            SketchPlane sketchPlane = SketchPlane.Create(_doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));

            foreach (XYZ[] corners in roomSquares)
            {
                // Build a CurveLoop for the square.
                CurveLoop squareLoop = new CurveLoop();
                squareLoop.Append(Line.CreateBound(corners[0], corners[1]));
                squareLoop.Append(Line.CreateBound(corners[1], corners[2]));
                squareLoop.Append(Line.CreateBound(corners[2], corners[3]));
                squareLoop.Append(Line.CreateBound(corners[3], corners[0]));

                // Use Clipper to compute the intersection (the in-bound portion)
                

                IList<CurveLoop> intersectedLoops = ClipperHelper.GetIntersection(_polygonLoop, squareLoop);

                // Compute original square area.
                double sideLength = corners[0].DistanceTo(corners[1]);
                double originalArea = sideLength * sideLength;

                // Calculate intersection area by extruding each resulting loop by 1 unit.
                double intersectionArea = 0.0;
                foreach (CurveLoop loop in intersectedLoops)
                {
                    Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                        new List<CurveLoop> { loop }, XYZ.BasisZ, 1.0);
                    intersectionArea += solid.Volume; // 1-unit thickness => volume equals area.
                }

                double trimmedOffArea = originalArea - intersectionArea;
                // You can store or log trimmedOffArea as needed.

                // Draw the resulting shape(s) in red.
                OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(255, 0, 0));

                foreach (CurveLoop loop in intersectedLoops)
                {
                    foreach (Curve c in loop)
                    {
                        DetailCurve detailCurve = _doc.Create.NewDetailCurve(_doc.ActiveView, c);
                        _doc.ActiveView.SetElementOverrides(detailCurve.Id, ogs);
                    }
                }
            }
            tx.Commit();
        }
    }
}


