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
                // Get user input for the number of rooms
                int numberOfRooms = GetNumberOfRooms();
                if (numberOfRooms <= 0)
                {
                    TaskDialog.Show("Error", "Invalid number of rooms.");
                    return Result.Failed;
                }

                // Generate points for each room
                List<XYZ> points = GenerateRandomPoints(numberOfRooms);

                // Visualize the points in Revit using model curves
                using (Transaction trans = new Transaction(doc, "Add Points as Model Curves"))
                {
                    trans.Start();
                    foreach (var point in points)
                    {
                        CreateModelCurve(doc, point);
                    }
                    trans.Commit();
                }

                TaskDialog.Show("Revit", $"{numberOfRooms} Rooms have been added.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;  // This will display the error in Revit
                TaskDialog.Show("Error", $"An error occurred: {ex.Message}");
                return Result.Failed;
            }
        }

        private int GetNumberOfRooms()
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox("Enter the number of rooms:", "User Input", "5");
            int numberOfRooms;
            if (int.TryParse(input, out numberOfRooms))
            {
                return numberOfRooms;
            }
            return -1;
        }

        private List<XYZ> GenerateRandomPoints(int numberOfRooms)
        {
            List<XYZ> points = new List<XYZ>();
            Random random = new Random();

            double maxX = 100;  // Define the maximum X and Y range for the points
            double maxY = 100;
            double minZ = 0;  // You can also randomize Z if needed
            double maxZ = 10;

            for (int i = 0; i < numberOfRooms; i++)
            {
                double randomX = random.NextDouble() * maxX;  // Generate a random X value between 0 and maxX
                double randomY = random.NextDouble() * maxY;  // Generate a random Y value between 0 and maxY
                double randomZ = random.NextDouble() * (maxZ - minZ) + minZ;  // Optional: Generate a random Z value

                points.Add(new XYZ(randomX, randomY, randomZ));
            }
            return points;
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

    }
}