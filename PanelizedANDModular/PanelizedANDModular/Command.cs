#region Namespaces
// These lines tell the program which libraries to use. 
// "System" and "System.Collections.Generic" provide basic tools like lists and common functions.
// The Autodesk.Revit.* libraries allow this code to interact with Revit's API (its programming interface).
// "System.Windows.Media" is used for things like colors.
using System;
using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows;
#endregion


public static class GlobalData
{
    public static double TotalRoomArea { get; set; }
    public static double LandArea { get; set; }
    public static HashSet<string> UniqueRoomTypesDisplayed = new HashSet<string>();
    public static int TextNoteUniqueCounter = 0;
    public static double landWidth { get; set; }
    public static double landHeight { get; set; }

}

// The namespace groups related classes together. Here, "PanelizedAndModularFinal" is the container for our code.
namespace PanelizedAndModularFinal
{
    // The Transaction attribute tells Revit that this command will make changes to the model,
    // and that we want to control when those changes are applied.
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        // The Execute method is the entry point for the command.
        // When you run the command in Revit, this method starts executing.
        // It receives necessary data such as the current application, active document, etc.
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Get a reference to the Revit application (uiapp) from the commandData.
            UIApplication uiapp = commandData.Application;
            // Get the active UI document (what the user sees) from the application.
            UIDocument uidoc = uiapp.ActiveUIDocument;
            // Get the active document (the actual Revit file with model data).
            Document doc = uidoc.Document;

            try
            {




                // --- Step: Get Land Area Input from the User ---
                LandInputWindow landWindow = new LandInputWindow();
                bool? landResult = landWindow.ShowDialog();
                if (landResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled land area input.");
                    return Result.Cancelled;
                }
                double userArea = landWindow.LandArea; // User input area (in sq ft)
                GlobalData.landWidth = landWindow.InputWidth;
                GlobalData.landHeight = landWindow.InputHeight;



                // --- Update the Active View's Crop Region Based on the User's Input ---
                // Calculate the side length of a square with the given area.
                double sideLength = Math.Sqrt(userArea);

                // Retrieve the active view and its current crop box.
                View activeView = doc.ActiveView;
                BoundingBoxXYZ cropBox = activeView.CropBox;

                // Calculate the center of the current crop box.
                XYZ center = new XYZ(
                    (cropBox.Min.X + cropBox.Max.X) / 2.0,
                    (cropBox.Min.Y + cropBox.Max.Y) / 2.0,
                    cropBox.Min.Z);

                // Calculate new min and max points.
                double halfSide = sideLength / 2.0;
                XYZ newMin = new XYZ(center.X - halfSide, center.Y - halfSide, cropBox.Min.Z);
                XYZ newMax = new XYZ(center.X + halfSide, center.Y + halfSide, cropBox.Max.Z);

                // Update the crop region inside a transaction.
                using (Transaction trans = new Transaction(doc, "Update Crop Region"))
                {
                    trans.Start();
                    activeView.CropBoxActive = true;
                    // Get the current crop box, modify its boundaries, and assign it back.
                    BoundingBoxXYZ newCropBox = activeView.CropBox;
                    newCropBox.Min = newMin;
                    newCropBox.Max = newMax;
                    activeView.CropBox = newCropBox;
                    trans.Commit();
                }

                // Now update the available layout area to match the new crop region.
                double availableLayoutArea = sideLength * sideLength;
                GlobalData.LandArea = availableLayoutArea;






                // Step 1: Get Room Inputs from User
                // Open a window to ask the user what types of rooms they want.
                RoomInputWindow firstWindow = new RoomInputWindow();
                // Show the window as a modal dialog. The result indicates if the user confirmed or canceled.
                bool? firstResult = firstWindow.ShowDialog();
                if (firstResult != true)
                {
                    // If the user canceled, show a message and exit the command.
                    TaskDialog.Show("Canceled", "User canceled at the first window.");
                    return Result.Cancelled;
                }

                // Retrieve the list of room types the user entered.
                List<RoomTypeRow> userSelections = firstWindow.RoomTypes;
                // Prepare an empty list to later store room instances.
                List<RoomInstanceRow> instanceRows = new List<RoomInstanceRow>();

                // Step 2: Generate Room Instances
                // Loop through each room type selected by the user.


                foreach (var row in userSelections)
                {
                    // If the requested quantity is 0 or negative, skip this room type.
                    if (row.Quantity <= 0) continue;
                    // For each requested room, create a new room instance.
                    for (int i = 0; i < row.Quantity; i++)
                    {
                        // Create a unique name for the room instance (e.g., "Office 1", "Office 2", etc.).
                        string instanceName = $"{row.Name} {i + 1}";
                        // Create a new instance object with the provided details.
                        var instance = new RoomInstanceRow
                        {
                            RoomType = row.Name,
                            Name = instanceName,
                            WpfColor = row.Color,
                            Area = 0.0 // Default area
                        };
                        // Add the created instance to our list.


                        instanceRows.Add(instance);
                    }
                }


                // If no room instances were created, inform the user and cancel the command.
                if (instanceRows.Count == 0)
                {
                    TaskDialog.Show("Info", "No rooms were requested.");
                    return Result.Cancelled;
                }

                // Step 3: Open Second Window for Room Adjustments
                // This window allows the user to adjust details like the area of each room instance.
                RoomInstancesWindow secondWindow = new RoomInstancesWindow(instanceRows);
                bool? secondResult = secondWindow.ShowDialog();
                if (secondResult != true)
                {
                    // If the user cancels, show a message and exit.
                    TaskDialog.Show("Canceled", "User canceled at the second window.");
                    return Result.Cancelled;
                }





                // Create a list to hold our room "nodes". Each node represents a room with its properties.
                List<SpaceNode> spaces = new List<SpaceNode>();
                // Create a Random object for generating random positions.
                Random random = new Random();

                // Process each room instance as adjusted by the user in the second window.
                foreach (var inst in secondWindow.Instances)
                {
                    // Ensure that the room area is not below 10 ft^2


                    double area = inst.Area < 10.0 ? 10.0 : inst.Area;

                    View activeView1 = doc.ActiveView;
                    // Declare a variable to hold the bounding box (the area limits) of the view.
                    BoundingBoxXYZ viewBox1 = null;
                    // If the view has a crop box active (user-defined boundary), use it.
                    if (activeView1.CropBoxActive && activeView1.CropBox != null)
                        viewBox1 = activeView1.CropBox;
                    else
                        // Otherwise, get the overall bounding box of the view.
                        viewBox1 = activeView1.get_BoundingBox(null);



                    double layoutWidth1 = viewBox1.Max.X - viewBox1.Min.X;
                    double layoutHeight1 = viewBox1.Max.Y - viewBox1.Min.Y;
                    // Calculate the total available area in the view.


                    XYZ position = new XYZ(viewBox1.Min.X + random.NextDouble() * layoutWidth1, viewBox1.Min.Y + random.NextDouble() * layoutHeight1, 0);

                    // Create a new SpaceNode object that holds all details about this room.
                    var node = new SpaceNode(inst.Name, inst.RoomType, area, position, inst.WpfColor);
                    // Add the node to our list.
                    spaces.Add(node);
                }

                // Retrieve the current view that is active in Revit.
                activeView = doc.ActiveView;
                // Declare a variable to hold the bounding box (the area limits) of the view.
                BoundingBoxXYZ viewBox = null;
                // If the view has a crop box active (user-defined boundary), use it.
                if (activeView.CropBoxActive && activeView.CropBox != null)
                    viewBox = activeView.CropBox;
                else
                    // Otherwise, get the overall bounding box of the view.
                    viewBox = activeView.get_BoundingBox(null);

                // NEW: Verify total room area fits within the available layout space
                // Calculate the total area of all rooms in square feet.
                double totalRoomArea = 0.0;
                foreach (var space in spaces)
                {
                    totalRoomArea += space.Area; // Each room's area is added.
                }

                double totalRoomAreaFt2 = totalRoomArea;
                GlobalData.TotalRoomArea = totalRoomArea;









                ////  Make Adjacency Matrix
                //// Open a window that allows the user to specify which rooms should be adjacent.
                //PreferredAdjacencyWindow adjacencyWindow = new PreferredAdjacencyWindow(spaces);
                //bool? result = adjacencyWindow.ShowDialog();
                //if (result != true)
                //{
                //    // If the user cancels, notify and exit.
                //    TaskDialog.Show("Canceled", "User canceled at the preferred adjacency matrix window.");
                //    return Result.Cancelled;
                //}

                //// Retrieve the preferred adjacency matrix from the window.
                //int[,] preferredAdjacency = adjacencyWindow.PreferredAdjacency;

                //// Step 4: Get Connectivity Matrix
                //// Open a window where the user can define which rooms should be connected.
                //ConnectivityMatrixWindow connectivityWindow = new ConnectivityMatrixWindow(spaces);
                //bool? connectivityResult = connectivityWindow.ShowDialog();
                //if (connectivityResult != true)
                //{
                //    // If the user cancels, show a message and exit.
                //    TaskDialog.Show("Canceled", "User canceled at the connectivity matrix window.");
                //    return Result.Cancelled;
                //}

                //// Retrieve the connectivity matrix from the window.
                //int[,] adjacencyMatrix = connectivityWindow.ConnectivityMatrix;

                //// Step 5: Open Edge Weights Window
                //// This window allows the user to assign weights (importance) to each connection between rooms.
                //EdgeWeightsWindow weightsWindow = new EdgeWeightsWindow(spaces, adjacencyMatrix);
                //bool? weightResult = weightsWindow.ShowDialog();
                //if (weightResult != true)
                //{
                //    // If the user cancels, notify and exit.
                //    TaskDialog.Show("Canceled", "User canceled the edge weights window.");
                //    return Result.Cancelled;
                //}

                //// Retrieve the weighted adjacency matrix which contains the connection strengths.
                //double?[,] weightedAdjMatrix = weightsWindow.WeightedAdjacencyMatrix;

                //// Apply a force-directed layout algorithm to adjust room positions.
                //// This simulates physical forces (like attraction and repulsion) so that rooms are well-spaced
                //// and the final layout respects user-defined connections and adjacencies.



                ////START FOR LOOP HERE FOR ALL THE COMBINATION POSSIBLE
                //ApplyForceDirectedLayout(spaces, preferredAdjacency, weightedAdjMatrix, viewBox);



                //SnapConnectedCircles(spaces, adjacencyMatrix);


                //ResolveCollisions(spaces);
                //CenterLayout(spaces, viewBox);



                //// Now create the connection lines (edges) between rooms using the new positions.
                //// Step 6: Create Room Connections with Weights
                //using (Transaction tx = new Transaction(doc, "Connect Rooms"))
                //{
                //    // Begin a new transaction so changes can be grouped.
                //    tx.Start();
                //    // Loop through each pair of rooms to check for a connection.
                //    for (int i = 0; i < spaces.Count; i++)
                //    {
                //        for (int j = i + 1; j < spaces.Count; j++)
                //        {
                //            // If a connection exists (i.e. a weight has been assigned) between room i and room j...
                //            if (weightedAdjMatrix[i, j].HasValue)
                //            {
                //                // Create a line (edge) between the two room positions.
                //                Line connectionLine = Line.CreateBound(spaces[i].Position, spaces[j].Position);
                //                // Define a plane using one room's position to know where to draw the line.
                //                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, spaces[i].Position);
                //                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                //                // Draw the connection line as a model curve in Revit.
                //                ModelCurve curve = doc.Create.NewModelCurve(connectionLine, sketchPlane);
                //            }
                //        }
                //    }
                //    // Commit the transaction to save the connection lines.
                //    tx.Commit();
                //}

                //// Step 7: Create Circular Rooms
                //// For each room, create a circular shape (with an outer square and a label).
                //using (Transaction tx = new Transaction(doc, "Create Rooms"))
                //{
                //    tx.Start();
                //    foreach (var space in spaces)
                //    {
                //        // Call the method to create the room's geometry.
                //        // Parameters include the room's position, area, color, name, and the active view's ID (for labeling).
                //        CreateCircleNode(doc, space.Position, space.Area, space.WpfColor, space.Name, uidoc.ActiveView.Id);
                //    }
                //    // Commit the transaction to save the room geometry.
                //    tx.Commit();
                //}

                //// Inform the user that the rooms and their connections have been successfully created.
                //TaskDialog.Show("Revit", $"Created {spaces.Count} room(s) with connections.");


                ////END OF STEP 1!!!!!!!!!
                //// END OF STEP 1!!!




















                //STEP 2 // STEP 2 // STEP 2 // STEP 2 // STEP 2 // STEP 2//
                ///////////////////////////////////////////////////////////
                ModuleInputWindow inputWindow = new ModuleInputWindow();
                bool? inputResult = inputWindow.ShowDialog();
                if (inputResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled the module input.");
                    return Result.Cancelled;
                }

           

                // Retrieve the stored user input values.
                double minWidth = inputWindow.MinWidth;
                double maxHeight = inputWindow.MaxHeight;

                // Now open the Module Types Window with the input values.
                // Now open the Module Types Window with the input values.
                ModuleTypesWindow typesWindow = new ModuleTypesWindow(minWidth, maxHeight);



                // Retrieve the list of ModuleType objects from ModuleTypesWindow.
                // (Ensure you’ve added a public property in ModuleTypesWindow, e.g., 
                //  public List<ModuleType> ModuleTypeList { get; private set; }.)
                List<ModuleType> moduleTypes = typesWindow.ModuleTypes;

           

                // STEP: Open the Module Combinations Window
                ModuleCombinationsWindow combWindow = new ModuleCombinationsWindow(moduleTypes, minWidth);


            

                bool? combResult = combWindow.ShowDialog();
                if (combResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled the module combination selection.");
                    return Result.Cancelled;
                }

                // Retrieve the user's selected combination (e.g., a string describing the modules).
                string selectedCombination = combWindow.SelectedCombination;
                TaskDialog.Show("Selected Combination", selectedCombination);



                ModuleArrangement arranger = new ModuleArrangement();
                arranger.CreateSquareLikeArrangement(doc, selectedCombination, moduleTypes);



                return Result.Succeeded;



            }
            catch (Exception ex)
            {
                // If any error occurs during execution, capture the error message and show it.
                message = ex.Message;
                TaskDialog.Show("Error", ex.Message);
                return Result.Failed;
            }
        }





















        //////////////////////////////////////////////////////////////////////////////////////////
        //---------------------------------------------------------------------------------------//
        //---------------------------------------------------------------------------------------//
        //----------------------------------METHOD METHOD METHOD METHODS BELOW-------------------//
        //---------------------------------------------------------------------------------------//
        //---------------------------------------------------------------------------------------//
        //////////////////////////////////////////////////////////////////////////////////////////




        ////////////////////////////////////////////////////////////////////////////////
        // Method: CreateCircleNode
        ////////////////////////////////////////////////////////////////////////////////
        // This method creates a visual representation of a room as a circle.
        // It also creates a surrounding square and a text note (label) with the room's name.
        // Parameters:
        // - doc: The current Revit document where the geometry will be created.
        // - position: The center position of the room.
        // - area: The area of the room (in square meters).
        // - wpfColor: The color for the room outline (from WPF).
        // - roomName: The name of the room (used for labeling).
        // - viewId: The ID of the active view where the label should appear.




        private void CreateCircleNode(Document doc, XYZ position, double area, System.Windows.Media.Color wpfColor, string roomName, ElementId viewId)
        {
            double areaFt2 = area;
            // Calculate the circle's radius using the formula: Area = π * r².
            double radius = Math.Sqrt(areaFt2 / Math.PI);
            // Define a plane at the room's position.
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, position);
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

            // Create the circle.
            Curve fullCircle = Ellipse.CreateCurve(position, radius, radius, XYZ.BasisX, XYZ.BasisY, 0, 2 * Math.PI);
            Autodesk.Revit.DB.Color revitColor = new Autodesk.Revit.DB.Color(wpfColor.R, wpfColor.G, wpfColor.B);
            GraphicsStyle gs = GetOrCreateLineStyle(doc, $"RoomStyle_{wpfColor}", revitColor);
            ModelCurve modelCurve = doc.Create.NewModelCurve(fullCircle, sketchPlane);
            modelCurve.LineStyle = gs;

            // Enhance circle appearance.
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineWeight(9);
            doc.ActiveView.SetElementOverrides(modelCurve.Id, ogs);

            // Create a square surrounding the circle.
            double d = radius;
            XYZ pt1 = new XYZ(position.X + d, position.Y + d, position.Z);
            XYZ pt2 = new XYZ(position.X - d, position.Y + d, position.Z);
            XYZ pt3 = new XYZ(position.X - d, position.Y - d, position.Z);
            XYZ pt4 = new XYZ(position.X + d, position.Y - d, position.Z);

            GraphicsStyle squareStyle = GetOrCreateLineStyle(doc, "SquareThinBlack", new Autodesk.Revit.DB.Color(0, 0, 0));
            ModelCurve squareCurve1 = doc.Create.NewModelCurve(Line.CreateBound(pt1, pt2), sketchPlane);
            squareCurve1.LineStyle = squareStyle;
            ModelCurve squareCurve2 = doc.Create.NewModelCurve(Line.CreateBound(pt2, pt3), sketchPlane);
            squareCurve2.LineStyle = squareStyle;
            ModelCurve squareCurve3 = doc.Create.NewModelCurve(Line.CreateBound(pt3, pt4), sketchPlane);
            squareCurve3.LineStyle = squareStyle;
            ModelCurve squareCurve4 = doc.Create.NewModelCurve(Line.CreateBound(pt4, pt1), sketchPlane);
            squareCurve4.LineStyle = squareStyle;

            // Create one text note per room type.
            // Extract the room type (assumes roomName like "Kitchen 1" -> "Kitchen").
            string roomType = roomName.Split(' ')[0];

            if (!GlobalData.UniqueRoomTypesDisplayed.Contains(roomType))
            {
                GlobalData.UniqueRoomTypesDisplayed.Add(roomType);

                // Get the active view from the provided viewId.
                View currentView = doc.GetElement(viewId) as View;
                BoundingBoxXYZ cropBox = currentView.CropBox;

                // Define an offset to the right of the crop box and vertical spacing.
                double offsetX = 5.0;          // Horizontal offset (adjust as needed).
                double noteHeightSpacing = 10.0; // Increased spacing to avoid overlap.!!!!!!!!!!!!!!!!!!!!!!!

                double noteX = cropBox.Max.X + offsetX;
                double noteY = cropBox.Max.Y - (GlobalData.TextNoteUniqueCounter * noteHeightSpacing);
                XYZ notePosition = new XYZ(noteX, noteY, cropBox.Min.Z);

                // Create the text note using the room type.
                FilteredElementCollector collector = new FilteredElementCollector(doc)
                                                        .OfClass(typeof(TextNoteType));
                TextNoteType textNoteType = collector.FirstElement() as TextNoteType;
                if (textNoteType != null)
                {
                    TextNote textNote = TextNote.Create(doc, viewId, notePosition, roomType, textNoteType.Id);

                    // Apply graphic override so the text note displays the designated room color.
                    OverrideGraphicSettings textOgs = new OverrideGraphicSettings();
                    textOgs.SetProjectionLineColor(revitColor);
                    doc.ActiveView.SetElementOverrides(textNote.Id, textOgs);
                }

                GlobalData.TextNoteUniqueCounter++;
            }
        }





        ////////////////////////////////////////////////////////////////////////////////
        // Method: GetOrCreateLineStyle
        ////////////////////////////////////////////////////////////////////////////////
        // This method checks if a line style (drawing appearance for lines) already exists.
        // If it does not exist, the method creates a new line style with the specified name and color.
        // Parameters:
        // - doc: The Revit document.
        // - styleName: The name to look for or assign to the new line style.
        // - revitColor: The color to set for the line style.
        private GraphicsStyle GetOrCreateLineStyle(Document doc, string styleName, Autodesk.Revit.DB.Color revitColor)
        {
            // Get the category that contains line styles.
            Category linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            Category subCat = null;
            // Check each subcategory to see if one matches the desired style name.
            foreach (Category c in linesCat.SubCategories)
            {
                if (c.Name == styleName)
                {
                    subCat = c;
                    break;
                }
            }

            // If a matching subcategory was not found, create a new one.
            if (subCat == null)
            {
                subCat = doc.Settings.Categories.NewSubcategory(linesCat, styleName);
            }

            // Use a sub-transaction to update the line color of the subcategory.
            using (SubTransaction st = new SubTransaction(doc))
            {
                st.Start();
                subCat.LineColor = revitColor;
                st.Commit();
            }

            // Return the graphics style associated with this subcategory for use in drawing.
            return subCat.GetGraphicsStyle(GraphicsStyleType.Projection);
        }

        // Below are constant values used in the force-directed layout algorithm.
        // They control how the rooms (nodes) interact with each other when arranging the layout.
        private const int ITERATIONS = 100;                // How many iterations (updates) the algorithm will run.
        private const double PREFERRED_ADJ_FACTOR = 10.0;     // Amplifies the attractive force if rooms are preferred to be adjacent.
        private const double SPRING_CONSTANT = 0.5;         // Controls the strength of attraction between connected rooms.
        private const double REPULSION_CONSTANT = 5.0;     // Controls how strongly rooms repel each other.
        private const double DAMPING = 0.85;                 // Reduces the movement speed of rooms to help the layout settle.

        // Constants for early stopping and adaptive damping.
        private const double MOVEMENT_THRESHOLD = 0.1;  // If average movement per node is below this, the algorithm stops early.
        private const bool ENABLE_ADAPTIVE_DAMPING = true; // Toggle to enable or disable adaptive damping.

        ////////////////////////////////////////////////////////////////////////////////
        // Method: ApplyForceDirectedLayout
        ////////////////////////////////////////////////////////////////////////////////
        // This method applies a force-directed layout algorithm to adjust room positions.
        // It simulates forces between room nodes:
        // - Repulsion prevents rooms from overlapping.
        // - Attraction pulls connected rooms closer.
        // - Preferred adjacency further increases the attractive force between specific rooms.
        // It then updates positions, resolves collisions, and ensures nodes stay within the view boundaries.
        // Parameters:
        // - spaces: The list of room nodes.
        // - preferredAdjMatrix: Matrix defining which room pairs are preferred to be adjacent.
        // - weightedAdjMatrix: Matrix defining the strength of connections between rooms.
        // - viewBox: The bounding box representing the visible layout area.
        private void ApplyForceDirectedLayout(List<SpaceNode> spaces,
                                      int[,] preferredAdjMatrix,
                                      double?[,] weightedAdjMatrix,
                                      BoundingBoxXYZ viewBox)
        {
            for (int iter = 0; iter < ITERATIONS; iter++)
            {
                // Adaptive damping adjustment.
                double currentDamping = DAMPING;
                if (ENABLE_ADAPTIVE_DAMPING)
                {
                    currentDamping = DAMPING - (DAMPING / 2.0) * (iter / (double)ITERATIONS);
                }

                // Initialize force vectors.
                XYZ[] forces = new XYZ[spaces.Count];
                for (int i = 0; i < forces.Length; i++)
                    forces[i] = XYZ.Zero;

                // Calculate forces between each pair of nodes.
                for (int i = 0; i < spaces.Count; i++)
                {
                    for (int j = i + 1; j < spaces.Count; j++)
                    {
                        XYZ posI = spaces[i].Position;
                        XYZ posJ = spaces[j].Position;
                        XYZ delta = posJ - posI;
                        double distance = delta.GetLength();
                        if (distance < 1e-6) distance = 1e-6; // Prevent division by zero.

                        // Repulsion force (inverse-square law).
                        double repForce = REPULSION_CONSTANT / (distance * distance);
                        XYZ repulsion = repForce * delta.Normalize();

                        // Determine connection weight.
                        double weight = 0.0;
                        if (weightedAdjMatrix[i, j].HasValue && weightedAdjMatrix[i, j].Value > 0)
                        {
                            weight = weightedAdjMatrix[i, j].Value;
                        }
                        if (preferredAdjMatrix[i, j] == 1)
                        {
                            // Ensure there is a weight even if none was defined.
                            weight = (weight == 0.0) ? 1.0 : weight * PREFERRED_ADJ_FACTOR;
                        }

                        // Determine desired distance.
                        // For preferred adjacencies, the rest length is the sum of the node radii so that the circles touch.
                        double desiredDistance = 1.0;
                        if (preferredAdjMatrix[i, j] == 1)
                        {
                            desiredDistance = spaces[i].Radius + spaces[j].Radius;
                        }

                        // Compute attractive (spring) force based on the difference from the desired distance.
                        double attrForce = 0.0;
                        if (weight > 0)
                        {
                            attrForce = SPRING_CONSTANT * weight * (distance - desiredDistance);
                        }
                        XYZ attraction = -attrForce * delta.Normalize();

                        // Net force for this pair.
                        XYZ forceIJ = repulsion + attraction;
                        forces[i] -= forceIJ;
                        forces[j] += forceIJ;
                    }
                }

                // Update positions based on forces.
                double totalMovement = 0.0;
                for (int i = 0; i < spaces.Count; i++)
                {
                    XYZ velocity = forces[i] * currentDamping;
                    double maxDisplacement = 5.0;
                    if (velocity.GetLength() > maxDisplacement)
                        velocity = velocity.Normalize() * maxDisplacement;

                    spaces[i].Position += velocity;
                    totalMovement += velocity.GetLength();
                }

                // Early stopping if movement is minimal.
                double averageMovement = totalMovement / spaces.Count;
                if (averageMovement < MOVEMENT_THRESHOLD)
                {
                    break;
                }

                // Collision 
                ResolveCollisions(spaces);


            }
        }


        private void ResolveCollisions(List<SpaceNode> spaces)
        {
            const double epsilon = 0.001;
            bool hasOverlap;

            do
            {
                hasOverlap = false;

                for (int i = 0; i < spaces.Count; i++)
                {
                    for (int j = i + 1; j < spaces.Count; j++)
                    {
                        XYZ posI = spaces[i].Position;
                        XYZ posJ = spaces[j].Position;

                        double radiusI = GetCircleRadius(spaces[i].Area);
                        double radiusJ = GetCircleRadius(spaces[j].Area);

                        XYZ delta = posJ - posI;
                        double distance = delta.GetLength();
                        double minDist = radiusI + radiusJ;

                        if (distance < minDist)
                        {
                            hasOverlap = true;
                            double overlap = (minDist - distance) + epsilon;
                            XYZ pushDir = (distance == 0) ? new XYZ(1, 0, 0) : delta.Normalize();

                            spaces[i].Position -= 0.5 * overlap * pushDir;
                            spaces[j].Position += 0.5 * overlap * pushDir;
                        }
                    }
                }
            } while (hasOverlap);
        }




        ////////////////////////////////////////////////////////////////////////////////
        // Method: GetCircleRadius
        ////////////////////////////////////////////////////////////////////////////////
        // Helper method to calculate the radius of a circle from its area.
        // The area is provided in square meters, and the method converts it to square feet before calculation.
        // Parameter:
        // - areaInM2: Area in square meters.
        // Returns:
        // - The computed radius of the circle.
        private double GetCircleRadius(double area)
        {

            return Math.Sqrt(area / Math.PI);
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Method: ClampNodesToCropRegion
        ////////////////////////////////////////////////////////////////////////////////
        // This method ensures that each room node remains within the defined layout area (crop region).
        // If a node is found outside the boundaries, it is moved back inside.
        // Parameters:
        // - spaces: The list of room nodes.
        // - bb: The bounding box defining the allowed layout area.
        //private void ClampNodesToCropRegion(List<SpaceNode> spaces, BoundingBoxXYZ bb)
        //{
        //    if (bb == null) return; // If there is no bounding box, exit the method.

        //    // Retrieve the minimum and maximum points of the bounding box.
        //    XYZ min = bb.Min;
        //    XYZ max = bb.Max;
        //    foreach (var space in spaces)
        //    {
        //        // Calculate the radius for the current node.
        //        double r = GetCircleRadius(space.Area);
        //        // Get the current position of the node.
        //        XYZ pos = space.Position;

        //        double x = pos.X;
        //        double y = pos.Y;
        //        double z = pos.Z;

        //        // Clamp the X coordinate so the circle stays within the left and right boundaries.
        //        x = Math.Max(x, min.X + r);
        //        x = Math.Min(x, max.X - r);

        //        // Clamp the Y coordinate similarly for the top and bottom boundaries.
        //        y = Math.Max(y, min.Y + r);
        //        y = Math.Min(y, max.Y - r);

        //        // The Z coordinate typically remains unchanged in a 2D layout.
        //        space.Position = new XYZ(x, y, z);
        //    }
        //}




        ////////////////////////////////////////////////////////////////////////////////
        // Method: SnapConnectedCircles
        ////////////////////////////////////////////////////////////////////////////////
        // This method iterates through all pairs of connected circles (nodes) as
        // defined by the connectivity matrix. If two connected circles are further
        // apart than the sum of their radii, it moves them closer until they are just
        // touching (within a small tolerance), ensuring they do not overlap.
        private void SnapConnectedCircles(List<SpaceNode> spaces, int[,] connectivityMatrix)
        {
            const double tolerance = 0.001;
            bool adjusted = true;
            int iterations = 0;
            int maxIterations = 10; // Prevents an endless loop in edge cases

            while (adjusted && iterations < maxIterations)
            {
                adjusted = false;
                for (int i = 0; i < spaces.Count; i++)
                {
                    for (int j = i + 1; j < spaces.Count; j++)
                    {
                        // Check if these two circles are connected.
                        if (connectivityMatrix[i, j] == 1)
                        {
                            double radiusI = GetCircleRadius(spaces[i].Area);
                            double radiusJ = GetCircleRadius(spaces[j].Area);
                            double desiredDistance = radiusI + radiusJ;

                            XYZ delta = spaces[j].Position - spaces[i].Position;
                            double currentDistance = delta.GetLength();

                            // If they're further apart than desired, move them closer.
                            if (currentDistance > desiredDistance + tolerance)
                            {
                                double moveAmount = (currentDistance - desiredDistance) / 2;
                                XYZ moveVector = delta.Normalize() * moveAmount;
                                spaces[i].Position += moveVector;
                                spaces[j].Position -= moveVector;
                                adjusted = true;
                            }
                        }
                    }
                }
                iterations++;
            }
        }














        private void CenterLayout(List<SpaceNode> spaces, BoundingBoxXYZ viewBox)
        {
            // Compute the bounding box of all nodes (including each circle's radius)
            XYZ layoutMin = new XYZ(double.MaxValue, double.MaxValue, 0);
            XYZ layoutMax = new XYZ(double.MinValue, double.MinValue, 0);
            foreach (var node in spaces)
            {
                double r = GetCircleRadius(node.Area);
                layoutMin = new XYZ(Math.Min(layoutMin.X, node.Position.X - r),
                                    Math.Min(layoutMin.Y, node.Position.Y - r), 0);
                layoutMax = new XYZ(Math.Max(layoutMax.X, node.Position.X + r),
                                    Math.Max(layoutMax.Y, node.Position.Y + r), 0);
            }

            // Calculate the center of the layout and the view
            XYZ layoutCenter = new XYZ((layoutMin.X + layoutMax.X) / 2.0, (layoutMin.Y + layoutMax.Y) / 2.0, 0);
            XYZ viewCenter = new XYZ((viewBox.Min.X + viewBox.Max.X) / 2.0, (viewBox.Min.Y + viewBox.Max.Y) / 2.0, 0);

            // Calculate the offset needed to center the layout
            XYZ offset = viewCenter - layoutCenter;

            // Apply the offset to all nodes
            foreach (var node in spaces)
            {
                node.Position += offset;
            }
        }

        private List<SpaceNode> CloneSpaces(List<SpaceNode> originalSpaces)
        {
            var cloned = new List<SpaceNode>();
            foreach (var node in originalSpaces)
            {
                // Create a new SpaceNode copying all properties. Ensure that any value type (like XYZ) is cloned.
                var newNode = new SpaceNode(
                    node.Name,
                    node.Function,
                    node.Area,
                    new XYZ(node.Position.X, node.Position.Y, node.Position.Z),
                    node.WpfColor)
                {
                    Radius = node.Radius
                };
                cloned.Add(newNode);
            }
            return cloned;
        }


        private BitmapSource GenerateThumbnailFromLayout(List<SpaceNode> layout)
        {
            // Create a canvas with fixed size.
            Canvas canvas = new Canvas { Width = 300, Height = 300 };

            // Draw each node as a small circle.
            foreach (var node in layout)
            {
                System.Windows.Shapes.Ellipse ellipse = new System.Windows.Shapes.Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = Brushes.Blue
                };


                // Ensure the positions are doubles.
                Canvas.SetLeft(ellipse, (double)node.Position.X);
                Canvas.SetTop(ellipse, (double)node.Position.Y);
                canvas.Children.Add(ellipse);
            }

            // Render the canvas to a bitmap.
            RenderTargetBitmap rtb = new RenderTargetBitmap(
                (int)canvas.Width, (int)canvas.Height,
                96, 96, PixelFormats.Pbgra32);
            canvas.Measure(new Size(canvas.Width, canvas.Height));
            canvas.Arrange(new Rect(new Size(canvas.Width, canvas.Height)));
            rtb.Render(canvas);

            return rtb;
        }
    }
}