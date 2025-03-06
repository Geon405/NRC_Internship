using Autodesk.Revit.DB;
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
        public void CreateSquareLikeArrangement(Document doc, string selectedCombination, List<ModuleType> moduleTypes)
        {
            // 1. Parse the user's combination string to find which modules to place.
            Dictionary<int, int> typeCounts = ParseCombination(selectedCombination);
            List<ModuleType> modulesToPlace = new List<ModuleType>();
            double totalArea = 0.0;

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

            // 2. Calculate a "target dimension" to guide row widths.
            double targetDim = Math.Sqrt(totalArea);

            // 3. Lay out modules in alternating rows: horizontal vs. vertical.
            double offsetX = 0.0, offsetY = 0.0;
            double rowHeight = 0.0;
            bool horizontalRow = true;

            // Keep track of all placed rectangles.
            List<XYZ[]> placedRectangles = new List<XYZ[]>();

            foreach (var mod in modulesToPlace)
            {
                double length = mod.Length;
                double width = mod.Width;
                double dimX = horizontalRow ? length : width;
                double dimY = horizontalRow ? width : length;

                // If adding this module exceeds targetDim in X, start a new row.
                if (offsetX + dimX > targetDim && offsetX > 0)
                {
                    offsetY += rowHeight;
                    offsetX = 0.0;
                    rowHeight = 0.0;
                    horizontalRow = !horizontalRow;
                    dimX = horizontalRow ? length : width;
                    dimY = horizontalRow ? width : length;
                }

                // Define module rectangle corners (in 2D, Z = 0).
                XYZ p1 = new XYZ(offsetX, offsetY, 0);
                XYZ p2 = new XYZ(offsetX + dimX, offsetY, 0);
                XYZ p3 = new XYZ(offsetX + dimX, offsetY + dimY, 0);
                XYZ p4 = new XYZ(offsetX, offsetY + dimY, 0);

                placedRectangles.Add(new XYZ[] { p1, p2, p3, p4 });

                offsetX += dimX;
                rowHeight = Math.Max(rowHeight, dimY);
            }

            // 4. Compute overall extents for centering.
            double maxX = placedRectangles.Max(rect => rect.Max(pt => pt.X));
            double maxY = placedRectangles.Max(rect => rect.Max(pt => pt.Y));
            double shiftX = -maxX / 2.0;
            double shiftY = -maxY / 2.0;

            // 5. Build geometry in a Revit transaction.
            using (Transaction trans = new Transaction(doc, "Create Module Arrangement"))
            {
                trans.Start();
                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
                SketchPlane sketch = SketchPlane.Create(doc, plane);

                // Create each module as a small extruded solid (height = 1 ft).
                foreach (var rect in placedRectangles)
                {
                    XYZ p1 = rect[0].Add(new XYZ(shiftX, shiftY, 0));
                    XYZ p2 = rect[1].Add(new XYZ(shiftX, shiftY, 0));
                    XYZ p3 = rect[2].Add(new XYZ(shiftX, shiftY, 0));
                    XYZ p4 = rect[3].Add(new XYZ(shiftX, shiftY, 0));
                    CreateModuleSolid(doc, p1, p2, p3, p4, 1.0);
                }
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
    }
}

