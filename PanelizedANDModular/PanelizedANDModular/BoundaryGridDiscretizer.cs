using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PanelizedAndModularFinal
{
    public class BoundaryGridDiscretizer
    {
        // Stores the ElementIds for the grid cell detail curves.
        public List<ElementId> SavedGridElementIds { get; private set; }

        /// <summary>
        /// Discretizes the building boundary (union of placed rectangles) into a grid.
        /// The cell dimension is computed as (minWidth / 3). For example, if minWidth is 15ft,
        /// then each cell will be 5ft x 5ft.
        /// Only cells that lie fully inside one of the module rectangles are drawn.
        /// </summary>
        public void CreateDiscretizedGrid(Document doc, List<XYZ[]> placedRectangles, double minWidth)
        {
            // Compute cell size as minWidth divided by 3.
            double cellSize = minWidth / 3.0;

            // Compute overall bounding box from the module rectangles.
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var rect in placedRectangles)
            {
                // rect[0] = bottom-left, rect[2] = top-right
                XYZ p1 = rect[0];
                XYZ p3 = rect[2];
                if (p1.X < minX) minX = p1.X;
                if (p1.Y < minY) minY = p1.Y;
                if (p3.X > maxX) maxX = p3.X;
                if (p3.Y > maxY) maxY = p3.Y;
            }

            int nCols = (int)Math.Ceiling((maxX - minX) / cellSize);
            int nRows = (int)Math.Ceiling((maxY - minY) / cellSize);
            List<ElementId> gridElementIds = new List<ElementId>();

            // Set up graphic overrides (blue grid lines) and a sketch plane.
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(0, 0, 255));
            Plane gridPlane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
            SketchPlane gridSketch = SketchPlane.Create(doc, gridPlane);

            // Loop through each potential grid cell.
            for (int i = 0; i < nCols; i++)
            {
                for (int j = 0; j < nRows; j++)
                {
                    double cellMinX = minX + i * cellSize;
                    double cellMinY = minY + j * cellSize;
                    double cellMaxX = cellMinX + cellSize;
                    double cellMaxY = cellMinY + cellSize;

                    // Check if this cell is fully inside at least one module rectangle.
                    if (IsCellInsideAnyModule(cellMinX, cellMinY, cellMaxX, cellMaxY, placedRectangles))
                    {
                        // Define the four corners of the cell.
                        XYZ p1 = new XYZ(cellMinX, cellMinY, 0);
                        XYZ p2 = new XYZ(cellMaxX, cellMinY, 0);
                        XYZ p3 = new XYZ(cellMaxX, cellMaxY, 0);
                        XYZ p4 = new XYZ(cellMinX, cellMaxY, 0);

                        // Create cell outline using four detail curves.
                        Line l1 = Line.CreateBound(p1, p2);
                        Line l2 = Line.CreateBound(p2, p3);
                        Line l3 = Line.CreateBound(p3, p4);
                        Line l4 = Line.CreateBound(p4, p1);

                        DetailCurve dc1 = doc.Create.NewDetailCurve(doc.ActiveView, l1);
                        DetailCurve dc2 = doc.Create.NewDetailCurve(doc.ActiveView, l2);
                        DetailCurve dc3 = doc.Create.NewDetailCurve(doc.ActiveView, l3);
                        DetailCurve dc4 = doc.Create.NewDetailCurve(doc.ActiveView, l4);

                        doc.ActiveView.SetElementOverrides(dc1.Id, ogs);
                        doc.ActiveView.SetElementOverrides(dc2.Id, ogs);
                        doc.ActiveView.SetElementOverrides(dc3.Id, ogs);
                        doc.ActiveView.SetElementOverrides(dc4.Id, ogs);

                        gridElementIds.Add(dc1.Id);
                        gridElementIds.Add(dc2.Id);
                        gridElementIds.Add(dc3.Id);
                        gridElementIds.Add(dc4.Id);
                    }
                }
            }
            SavedGridElementIds = gridElementIds;
        }

        /// <summary>
        /// Returns true if the entire bounding box of the cell lies inside at least one rectangle.
        /// </summary>
        private bool IsCellInsideAnyModule(
            double cellMinX, double cellMinY,
            double cellMaxX, double cellMaxY,
            List<XYZ[]> rects)
        {
            foreach (var rect in rects)
            {
                // Extract min/max from this rectangle
                double rectMinX = Math.Min(rect[0].X, rect[2].X);
                double rectMinY = Math.Min(rect[0].Y, rect[2].Y);
                double rectMaxX = Math.Max(rect[0].X, rect[2].X);
                double rectMaxY = Math.Max(rect[0].Y, rect[2].Y);

                // If the entire cell fits within this rect, we're good
                if (cellMinX >= rectMinX - 1e-9 &&
                    cellMaxX <= rectMaxX + 1e-9 &&
                    cellMinY >= rectMinY - 1e-9 &&
                    cellMaxY <= rectMaxY + 1e-9)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Overload that accepts a list of boundary ElementIds (from the red boundary detail curves).
        /// This extracts a bounding box polygon and then calls the main method.
        /// Note: Using this overload only approximates the union from the bounding box.
        /// </summary>
        public void CreateDiscretizedGrid(Document doc, List<ElementId> savedBoundaryIds, double minWidth)
        {
            List<XYZ[]> boundaryPolygons = ExtractBoundaryPolygons(doc, savedBoundaryIds);
            // Using the extracted boundary polygon as if it were the placed rectangles.
            CreateDiscretizedGrid(doc, boundaryPolygons, minWidth);
        }

        /// <summary>
        /// Extracts a single bounding box polygon from the given detail curves.
        /// </summary>
        private List<XYZ[]> ExtractBoundaryPolygons(Document doc, List<ElementId> boundaryIds)
        {
            List<XYZ> pts = new List<XYZ>();
            foreach (var id in boundaryIds)
            {
                DetailCurve dc = doc.GetElement(id) as DetailCurve;
                if (dc != null)
                {
                    Line line = dc.GeometryCurve as Line;
                    if (line != null)
                    {
                        pts.Add(line.GetEndPoint(0));
                        pts.Add(line.GetEndPoint(1));
                    }
                }
            }
            if (pts.Count == 0)
                return new List<XYZ[]>();

            double minX = pts.Min(p => p.X);
            double minY = pts.Min(p => p.Y);
            double maxX = pts.Max(p => p.X);
            double maxY = pts.Max(p => p.Y);

            XYZ bottomLeft = new XYZ(minX, minY, 0);
            XYZ bottomRight = new XYZ(maxX, minY, 0);
            XYZ topRight = new XYZ(maxX, maxY, 0);
            XYZ topLeft = new XYZ(minX, maxY, 0);
            return new List<XYZ[]>
            {
                new XYZ[] { bottomLeft, bottomRight, topRight, topLeft }
            };
        }
    }
}
