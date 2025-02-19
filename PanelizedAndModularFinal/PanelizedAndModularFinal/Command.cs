#region Namespaces
using System; // Provides fundamental classes and base data types.
using System.Collections.Generic; // Provides generic collection classes.
using System.Diagnostics; // Provides classes for debugging and tracing.
using Autodesk.Revit.ApplicationServices; // Provides access to Revit application services.
using Autodesk.Revit.Attributes; // Provides attributes used by Revit commands.
using Autodesk.Revit.DB; // Provides access to Revit’s database (elements, geometry, etc.).
using Autodesk.Revit.UI; // Provides classes for interacting with Revit’s user interface.
using Autodesk.Revit.UI.Selection; // Provides selection-related classes for Revit.
using System.Windows.Media; // Provides support for WPF colors.
#endregion

namespace PanelizedAndModularFinal
{
    // Define a Revit external command with a manual transaction mode.
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        // Main entry point for the external command.
        public Result Execute(ExternalCommandData commandData,
                              ref string message,
                              ElementSet elements)
        {
            // Get the Revit UI application, active document, and document objects.
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // Open the first window (a WPF dialog) to get user input for room types.
                //firstWindow will initialize all 12 room types names with their designated color
                RoomInputWindow firstWindow = new RoomInputWindow();
                bool? firstResult = firstWindow.ShowDialog();
                // If the user cancels or closes the window, show a message and cancel the command.
                if (firstResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled at the first window.");
                    return Result.Cancelled;
                }

                // Retrieve the list of room types the user selected.
                //userSelections is a list of RoomTypeRow where we can get room name, color and # of space
                List<RoomTypeRow> userSelections = firstWindow.RoomTypes;
                // Initialize a list to hold room instance rows based on user selections.
                List<RoomInstanceRow> instanceRows = new List<RoomInstanceRow>();

                // Loop through each room type selected by the user.
                foreach (var row in userSelections)
                {
                    // Skip any room type where the quantity is less than or equal to zero.
                    if (row.Quantity <= 0)
                        continue;//skip the specified room

                    // Create the specified number of room instances for each room type.
                    //if user inputted 5 bedroom then we should get bedroom1, bedroom2, bedroom3...
                    for (int i = 0; i < row.Quantity; i++)
                    {
                        // Generate a unique name for each instance (e.g., "RoomName 1").
                        string instanceName = $"{row.Name} {i + 1}";
                        // Create a new room instance with default area value (20.0) and specified color.
                        var instance = new RoomInstanceRow
                        {
                            RoomType = row.Name,
                            Name = instanceName,
                            WpfColor = row.Color,
                            Area = 20.0
                        };
                        // Add the created instance to the list.
                        //each space is now added to the list, so we have access to example the attributes of bedroom1 or kitchen2 etc.
                        instanceRows.Add(instance);
                    }
                }

                // If no valid room instances were created, inform the user and cancel the command.
                if (instanceRows.Count == 0)
                {
                    TaskDialog.Show("Info", "No rooms were requested.");
                    return Result.Cancelled;
                }

                // Open the second window (a WPF dialog) for user adjustments on the room instances.
                RoomInstancesWindow secondWindow = new RoomInstancesWindow(instanceRows);
                bool? secondResult = secondWindow.ShowDialog();
                // If the user cancels or closes this window, show a message and cancel the command.
                if (secondResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled at the second window.");
                    return Result.Cancelled;
                }

                // Prepare a list to store space nodes that represent rooms with their properties.
                List<SpaceNode> spaces = new List<SpaceNode>();
                // Initialize a random number generator to assign random positions to rooms.
                Random random = new Random();

                // Loop through each room instance after user modifications.
                foreach (var inst in secondWindow.Instances)
                {
                    // Ensure the room's area is at least 10.0; if not, override it.
                    double area = inst.Area < 10.0 ? 10.0 : inst.Area;
                    // Generate a random position in the X-Y plane (Z is 0).
                    XYZ position = new XYZ(random.NextDouble() * 100, random.NextDouble() * 100, 0);
                    // Create a new space node representing the room.
                    var node = new SpaceNode(inst.Name, inst.RoomType, area, position, inst.WpfColor);
                    // Add the node to the list.
                    spaces.Add(node);
                }

                //---------------------code for connectivity matrix should be here--------------------

                ConnectivityMatrixWindow connectivityWindow = new ConnectivityMatrixWindow(spaces);
                bool? connectivityResult = connectivityWindow.ShowDialog();
                if (connectivityResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled at the connectivity matrix window.");
                    return Result.Cancelled;
                }

                // Retrieve the adjacency matrix (0/1) from the window.
                int[,] adjacencyMatrix = connectivityWindow.ConnectivityMatrix;
                // Now 'adjacencyMatrix[i, j]' tells you if 'spaces[i]' is connected to 'spaces[j]'.

                // Connect circles based on the connectivity matrix.
                using (Transaction tx = new Transaction(doc, "Connect Rooms"))
                {
                    tx.Start();
                    for (int i = 0; i < spaces.Count; i++)
                    {
                        for (int j = i + 1; j < spaces.Count; j++)
                        {
                            if (adjacencyMatrix[i, j] == 1)
                            {
                                Line connectionLine = Line.CreateBound(spaces[i].Position, spaces[j].Position);
                                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, spaces[i].Position);
                                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                                doc.Create.NewModelCurve(connectionLine, sketchPlane);
                            }
                        }
                    }
                    tx.Commit();
                }






                ////STILL NOT SURE ABOUT EDGE WEIGHT CONNECTIVITYYY!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
                ////------------------------code for edge weight connectivity---------------------------------

                //// After retrieving adjacencyMatrix from ConnectivityMatrixWindow...
                //EdgeWeightsWindow weightsWindow = new EdgeWeightsWindow(spaces, adjacencyMatrix);
                //bool? weightResult = weightsWindow.ShowDialog();
                //if (weightResult != true)
                //{
                //    TaskDialog.Show("Canceled", "User canceled the edge weights window.");
                //    return Result.Cancelled;
                //}

                //// This is your weighted adjacency data.
                //double?[,] weightedAdjMatrix = weightsWindow.WeightedAdjacencyMatrix;
                ////----------------------------------------------------------------------------------------------

                // Continue with creating geometry, etc.
                using (Transaction tx = new Transaction(doc, "Create Rooms"))
                {
                    tx.Start();
                    foreach (var space in spaces)
                    {
                        CreateCircleNode(doc, space.Position, space.Area, space.WpfColor);
                    }
                    tx.Commit();
                }

                // Begin a Revit transaction to create the room geometry in the document.
                using (Transaction tx = new Transaction(doc, "Create Rooms"))
                {
                    tx.Start();
                    // For each space node, create a circular representation in the document.
                    foreach (var space in spaces)
                    {
                        CreateCircleNode(doc, space.Position, space.Area, space.WpfColor);
                    }
                    tx.Commit(); // Commit the transaction to apply changes.
                }

                // Inform the user that the rooms have been created.
                TaskDialog.Show("Revit", $"Created {spaces.Count} room(s).");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                // If an error occurs, set the error message, show an error dialog, and return a failure result.
                message = ex.Message;
                TaskDialog.Show("Error", ex.Message);
                return Result.Failed;
            }
        }

        // Method that creates a circular room representation (using two arcs) in the Revit document.
        private void CreateCircleNode(Document doc, XYZ position, double area, System.Windows.Media.Color wpfColor)
        {
            // Convert the area from square meters to square feet.
            double areaFt2 = area * 10.7639;
            // Calculate the circle's radius using the area formula (A = πr²).
            double radius = Math.Sqrt(areaFt2 / Math.PI);
            // Create a plane at the given position with the normal vector along the Z-axis.
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, position);
            // Create a sketch plane on which the circle will be drawn.
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

            // Determine key points on the circle: right, up, left, and down.
            XYZ right = position + new XYZ(radius, 0, 0);
            XYZ up = position + new XYZ(0, radius, 0);
            XYZ left = position + new XYZ(-radius, 0, 0);
            // 'Down' is computed as the mirror of 'up' relative to the center.
            XYZ down = position - (up - position);

            // Create two arcs that together form a complete circle.
            Arc arc1 = Arc.Create(right, left, up);
            Arc arc2 = Arc.Create(left, right, down);

            // Convert the WPF color to a Revit-specific color.
            Autodesk.Revit.DB.Color revitColor = new Autodesk.Revit.DB.Color(wpfColor.R, wpfColor.G, wpfColor.B);
            // Retrieve or create a line style for the room using the specified color.
            GraphicsStyle gs = GetOrCreateLineStyle(doc, $"RoomStyle_{wpfColor}", revitColor);

            // Create model curves (the arcs) in the document on the sketch plane.
            ModelCurve mc1 = doc.Create.NewModelCurve(arc1, sketchPlane);
            ModelCurve mc2 = doc.Create.NewModelCurve(arc2, sketchPlane);

            // Assign the custom line style to both model curves.
            mc1.LineStyle = gs;
            mc2.LineStyle = gs;
        }

        // Method to get an existing line style or create a new one with the provided name and color.
        GraphicsStyle GetOrCreateLineStyle(Document doc, string styleName, Autodesk.Revit.DB.Color revitColor)
        {
            // Access the main Lines category from the document settings.
            Category linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            Category subCat = null;
            // Iterate through the subcategories to check if one matches the desired style name.
            foreach (Category c in linesCat.SubCategories)
            {
                if (c.Name == styleName)
                {
                    subCat = c;
                    break;
                }
            }

            // If the subcategory does not exist, create a new one.
            if (subCat == null)
            {
                subCat = doc.Settings.Categories.NewSubcategory(linesCat, styleName);
            }

            // Use a sub-transaction to safely update the subcategory's line color.
            using (SubTransaction st = new SubTransaction(doc))
            {
                st.Start();
                subCat.LineColor = revitColor;
                st.Commit();
            }

            // Return the graphics style (projection type) for the created or found subcategory.
            return subCat.GetGraphicsStyle(GraphicsStyleType.Projection);
        }
    }
}
