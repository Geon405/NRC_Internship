#region Namespaces
using System;
using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Media;

#endregion

namespace PanelizedAndModularFinal
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // Step 1: Get Room Inputs from User
                RoomInputWindow firstWindow = new RoomInputWindow();
                bool? firstResult = firstWindow.ShowDialog();
                if (firstResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled at the first window.");
                    return Result.Cancelled;
                }

                List<RoomTypeRow> userSelections = firstWindow.RoomTypes;
                List<RoomInstanceRow> instanceRows = new List<RoomInstanceRow>();

                // Step 2: Generate Room Instances
                foreach (var row in userSelections)
                {
                    if (row.Quantity <= 0) continue;
                    for (int i = 0; i < row.Quantity; i++)
                    {
                        string instanceName = $"{row.Name} {i + 1}";
                        var instance = new RoomInstanceRow
                        {
                            RoomType = row.Name,
                            Name = instanceName,
                            WpfColor = row.Color,
                            Area = 20.0
                        };
                        instanceRows.Add(instance);
                    }
                }

                if (instanceRows.Count == 0)
                {
                    TaskDialog.Show("Info", "No rooms were requested.");
                    return Result.Cancelled;
                }

                // Step 3: Open Second Window for Room Adjustments
                RoomInstancesWindow secondWindow = new RoomInstancesWindow(instanceRows);
                bool? secondResult = secondWindow.ShowDialog();
                if (secondResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled at the second window.");
                    return Result.Cancelled;
                }

                List<SpaceNode> spaces = new List<SpaceNode>();
                Random random = new Random();

                foreach (var inst in secondWindow.Instances)
                {
                    double area = inst.Area < 10.0 ? 10.0 : inst.Area;
                    XYZ position = new XYZ(random.NextDouble() * 100, random.NextDouble() * 100, 0);
                    var node = new SpaceNode(inst.Name, inst.RoomType, area, position, inst.WpfColor);
                    spaces.Add(node);
                }

                // Step 4: Get Connectivity Matrix
                ConnectivityMatrixWindow connectivityWindow = new ConnectivityMatrixWindow(spaces);
                bool? connectivityResult = connectivityWindow.ShowDialog();
                if (connectivityResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled at the connectivity matrix window.");
                    return Result.Cancelled;
                }

                int[,] adjacencyMatrix = connectivityWindow.ConnectivityMatrix;

                // Step 5: Open Edge Weights Window
                EdgeWeightsWindow weightsWindow = new EdgeWeightsWindow(spaces, adjacencyMatrix);
                bool? weightResult = weightsWindow.ShowDialog();
                if (weightResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled the edge weights window.");
                    return Result.Cancelled;
                }

                // Retrieve weighted adjacency matrix
                double?[,] weightedAdjMatrix = weightsWindow.WeightedAdjacencyMatrix;

                // Step 6: Create Room Connections with Weights
                using (Transaction tx = new Transaction(doc, "Connect Rooms"))
                {
                    tx.Start();
                    for (int i = 0; i < spaces.Count; i++)
                    {
                        for (int j = i + 1; j < spaces.Count; j++)
                        {
                            if (weightedAdjMatrix[i, j].HasValue)
                            {
                                Line connectionLine = Line.CreateBound(spaces[i].Position, spaces[j].Position);
                                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, spaces[i].Position);
                                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                                ModelCurve curve = doc.Create.NewModelCurve(connectionLine, sketchPlane);
                            }
                        }
                    }
                    tx.Commit();
                }

                // Step 7: Create Circular Rooms
                using (Transaction tx = new Transaction(doc, "Create Rooms"))
                {
                    tx.Start();
                    foreach (var space in spaces)
                    {
                        CreateCircleNode(doc, space.Position, space.Area, space.WpfColor);
                    }
                    tx.Commit();
                }

                TaskDialog.Show("Revit", $"Created {spaces.Count} room(s) with connections.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", ex.Message);
                return Result.Failed;
            }
        }

        // Method to create circular room representation
        private void CreateCircleNode(Document doc, XYZ position, double area, System.Windows.Media.Color wpfColor)
        {
            double areaFt2 = area * 10.7639;
            double radius = Math.Sqrt(areaFt2 / Math.PI);
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, position);
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

            Curve fullCircle = Ellipse.CreateCurve(position, radius, radius, XYZ.BasisX, XYZ.BasisY, 0, 2 * Math.PI);
            Autodesk.Revit.DB.Color revitColor = new Autodesk.Revit.DB.Color(wpfColor.R, wpfColor.G, wpfColor.B);
            GraphicsStyle gs = GetOrCreateLineStyle(doc, $"RoomStyle_{wpfColor}", revitColor);
            ModelCurve modelCurve = doc.Create.NewModelCurve(fullCircle, sketchPlane);
            modelCurve.LineStyle = gs;
        }

        // Method to get or create a line style
        private GraphicsStyle GetOrCreateLineStyle(Document doc, string styleName, Autodesk.Revit.DB.Color revitColor)
        {
            Category linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            Category subCat = null;
            foreach (Category c in linesCat.SubCategories)
            {
                if (c.Name == styleName)
                {
                    subCat = c;
                    break;
                }
            }

            if (subCat == null)
            {
                subCat = doc.Settings.Categories.NewSubcategory(linesCat, styleName);
            }

            using (SubTransaction st = new SubTransaction(doc))
            {
                st.Start();
                subCat.LineColor = revitColor;
                st.Commit();
            }

            return subCat.GetGraphicsStyle(GraphicsStyleType.Projection);
        }
    }
}

