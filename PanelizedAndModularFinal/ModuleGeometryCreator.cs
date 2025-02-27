using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace PanelizedAndModularFinal
{
    /// <summary>
    /// This class is responsible for creating rectangles in the Revit model
    /// based on the user's chosen module combination.
    /// </summary>
    public static class ModuleGeometryCreator
    {
        /// <summary>
        /// Main entry point: draws the rectangles for each module in the chosen combination.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <param name="combinationString">User's chosen combination, e.g. "1 x Module_Type 2 + 1 x Module_Type 1 = 1125 ft²".</param>
        /// <param name="moduleTypes">The list of available ModuleType objects.</param>
        public static void CreateRectanglesForCombination(Document doc,
                                                          string combinationString,
                                                          List<ModuleType> moduleTypes)
        {
            // Parse the combination string to figure out how many of each module type are used.
            Dictionary<int, int> moduleCounts = ParseCombinationString(combinationString);

            // Get an origin point (for example, near the bottom-left of the active view).
            View activeView = doc.ActiveView;
            BoundingBoxXYZ viewBox = activeView.CropBoxActive && activeView.CropBox != null
                                     ? activeView.CropBox
                                     : activeView.get_BoundingBox(null);

            // Starting point near the lower-left corner of the view.
            XYZ startPoint = new XYZ(viewBox.Min.X + 10, viewBox.Min.Y + 10, 0);
            double currentXOffset = 0.0; // We'll place modules horizontally side-by-side

            using (Transaction tx = new Transaction(doc, "Create Module Rectangles"))
            {
                tx.Start();

                // Loop through each module type that appears in the combination
                foreach (var kvp in moduleCounts)
                {
                    int moduleTypeId = kvp.Key;   // e.g., 1, 2, 3
                    int count = kvp.Value;        // how many of that module type

                    // Find the matching ModuleType object
                    ModuleType mt = moduleTypes.Find(m => m.ID == moduleTypeId);
                    if (mt == null) continue;

                    // For each instance of this module type, draw one rectangle
                    for (int i = 0; i < count; i++)
                    {
                        double width = mt.Width;     // e.g. 15
                        double length = mt.Length;   // e.g. 45

                        // Lower-left corner for this rectangle
                        XYZ rectOrigin = new XYZ(startPoint.X + currentXOffset, startPoint.Y, 0);

                        // Create the rectangle geometry
                        CreateRectangle(doc, rectOrigin, width, length, mt.Area);

                        // Shift horizontally for the next rectangle
                        currentXOffset += width;
                    }
                }

                tx.Commit();
            }
        }

        /// <summary>
        /// Creates a single rectangle on the XY plane (Z=0) in Revit using ModelCurves.
        /// Optionally places a text note showing the module's area.
        /// </summary>
        private static void CreateRectangle(Document doc,
                                            XYZ origin,
                                            double width,
                                            double length,
                                            double area)
        {
            // Create a sketch plane at Z=0
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, origin);
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

            // Define rectangle corners (bottom-left, bottom-right, top-right, top-left)
            XYZ ptA = origin;
            XYZ ptB = new XYZ(origin.X + width, origin.Y, origin.Z);
            XYZ ptC = new XYZ(origin.X + width, origin.Y + length, origin.Z);
            XYZ ptD = new XYZ(origin.X, origin.Y + length, origin.Z);

            // Create lines for each edge
            Line lineAB = Line.CreateBound(ptA, ptB);
            Line lineBC = Line.CreateBound(ptB, ptC);
            Line lineCD = Line.CreateBound(ptC, ptD);
            Line lineDA = Line.CreateBound(ptD, ptA);

            // Create the model curves in the document
            doc.Create.NewModelCurve(lineAB, sketchPlane);
            doc.Create.NewModelCurve(lineBC, sketchPlane);
            doc.Create.NewModelCurve(lineCD, sketchPlane);
            doc.Create.NewModelCurve(lineDA, sketchPlane);

            // Optionally add a text note for area
            TextNoteType textNoteType = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .FirstElement() as TextNoteType;
            if (textNoteType != null)
            {
                // Place text in the center of the rectangle
                XYZ textPos = new XYZ(origin.X + width / 2, origin.Y + length / 2, origin.Z);
                TextNote.Create(doc, doc.ActiveView.Id, textPos, $"Area: {area} ft²", textNoteType.Id);
            }
        }

        /// <summary>
        /// Simple parser that reads a combination string like:
        /// "1 x Module_Type 2 + 1 x Module_Type 1 = 1125 ft²"
        /// and returns a dictionary of moduleTypeID -> count.
        /// </summary>
        private static Dictionary<int, int> ParseCombinationString(string combinationString)
        {
            // Example input: "1 x Module_Type 2 + 1 x Module_Type 1 = 1125 ft²"
            Dictionary<int, int> result = new Dictionary<int, int>();

            // Remove everything after '=' (the total area portion)
            int eqIndex = combinationString.IndexOf('=');
            string modulesPart = eqIndex > 0
                ? combinationString.Substring(0, eqIndex).Trim()
                : combinationString;

            // Split by '+' to get individual module segments
            string[] parts = modulesPart.Split(new char[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                // e.g. "1 x Module_Type 2"
                string trimmed = part.Trim();
                // We'll split by spaces
                // => indices: [0] -> "1", [1] -> "x", [2] -> "Module_Type", [3] -> "2"
                string[] tokens = trimmed.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 4)
                {
                    // parse count
                    if (int.TryParse(tokens[0], out int count))
                    {
                        // parse module type ID
                        if (int.TryParse(tokens[3], out int modID))
                        {
                            if (!result.ContainsKey(modID))
                                result[modID] = 0;
                            result[modID] += count;
                        }
                    }
                }
            }
            return result;
        }
    }
}
