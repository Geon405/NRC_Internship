#region Namespaces
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

#endregion

namespace PanelizedANDModular
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(
        ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            try
            {
                // Get user input for the number of spaces
                int numberOfRooms = GetNumberOfSpaces();
                if (numberOfRooms <= 0)
                {
                    TaskDialog.Show("Error", "Invalid number of rooms.");
                    return Result.Failed;
                }

                // Generate spaces based on user input
                List<SpaceNode> spaces = GetUserDefinedSpaces(numberOfRooms);

                // Visualize spaces as circles in Revit
                using (Transaction trans = new Transaction(doc, "Visualize Circle Spaces"))
                {
                    trans.Start();
                    foreach (var space in spaces)
                    {
                        CreateModelCurve(doc, space.Position);  // Visualize each space with a vertical line
                        TaskDialog.Show("Space Info", $"Function: {space.Function}\nName: {space.Name}\nArea: {space.Area:F2} m²");
                    }
                    trans.Commit();
                }

                TaskDialog.Show("Revit", $"{numberOfRooms} Rooms have been added.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", $"An error occurred: {ex.Message}");
                return Result.Failed;
            }
        }

        private int GetNumberOfSpaces()
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox("Enter the number of spaces:", "Number of Spaces", "5");
            int numberOfSpaces;
            if (int.TryParse(input, out numberOfSpaces))
            {
                return numberOfSpaces;
            }
            return -1;
        }

        private class SpaceNode
        {
            public string Name { get; set; }
            public string Function { get; set; }
            public double Area { get; set; }
            public XYZ Position { get; set; }

            public SpaceNode(string name, string function, double area, XYZ position)
            {
                Name = name;
                Function = function;
                Area = area;
                Position = position;
            }
        }
        private List<SpaceNode> GetUserDefinedSpaces(int numberOfSpaces)
        {
            List<SpaceNode> spaces = new List<SpaceNode>();

            for (int i = 0; i < numberOfSpaces; i++)
            {
                // Get Space Function
                string function = Microsoft.VisualBasic.Interaction.InputBox($"Enter the function for Space {i + 1} (e.g., Bedroom, Kitchen, Living Room):", "Space Function", "Bedroom");

                // Get Space Name
                string name = Microsoft.VisualBasic.Interaction.InputBox($"Enter the name for {function} {i + 1} (or leave blank to use the function name):", "Space Name", $"{function} {i + 1}");
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = function;  // Use function as the name if left blank
                }

                // Get Space Area
                string areaInput = Microsoft.VisualBasic.Interaction.InputBox($"Enter the area (in m²) for {name}:", "Space Area", "20");
                double area;
                if (!double.TryParse(areaInput, out area) || area <= 0)
                {
                    TaskDialog.Show("Error", "Invalid area input. Setting area to 10 m².");
                    area = 10;  // Default value if input is invalid
                }

                // Generate a random position for now
                Random random = new Random();
                XYZ position = new XYZ(random.NextDouble() * 100, random.NextDouble() * 100, 0);

                // Add the space to the list
                spaces.Add(new SpaceNode(name, function, area, position));
            }

            return spaces;
        }

        private void CreateModelCurve(Document doc, XYZ position)
        {
            try
            {
                XYZ endPosition = position + new XYZ(0, 0, 1);  // Short vertical line
                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisY, position);  // Use a YZ plane for vertical lines
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                Line line = Line.CreateBound(position, endPosition);
                doc.Create.NewModelCurve(line, sketchPlane);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error in CreateModelCurve", ex.Message);
            }
        }

        //private void CreateCircleShape(Document doc, SpaceNode space)
        //{
        //    try
        //    {
        //        // Calculate the radius of the circle based on the area
        //        double radius = Math.Sqrt(space.Area / Math.PI);
        //        XYZ center = space.Position;

        //        // Create a circular arc in the XY plane
        //        Arc circle = Arc.Create(center, radius, 0, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY);
        //        List<Curve> circleEdges = new List<Curve> { circle };

        //        // Create a DirectShape element
        //        DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
        //        ds.ApplicationId = "CircleSpace";
        //        ds.ApplicationDataId = space.Name;

        //        // Set the shape (circle as a DirectShape)
        //        ds.SetShape(circleEdges);

        //        // Add custom information in the Comments parameter
        //        ds.LookupParameter("Comments").Set($"Name: {space.Name}, Function: {space.Function}, Area: {space.Area:F2} m²");
        //    }
        //    catch (Exception ex)
        //    {
        //        TaskDialog.Show("Error in CreateCircleShape", ex.Message);
        //    }
        //}
    }
}