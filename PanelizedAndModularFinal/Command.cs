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
using PanelizedAndModularFinal;
#endregion

public static class GlobalData
{
    public static double TotalRoomArea { get; set; }
    public static double LandArea { get; set; }
    public static HashSet<string> UniqueRoomTypesDisplayed = new HashSet<string>();
    public static int TextNoteUniqueCounter = 0;
    public static double landWidth { get; set; }
    public static double landHeight { get; set; }

    public static double moduleWidth { get; set; }

    public static List<SpaceNode> SavedSpaces { get; set; }
    public static List<ElementId> SavedConnectionLines { get; set; } = new List<ElementId>();

    // New property to hold all elements created in Step 1
    public static List<ElementId> Step1Elements { get; set; } = new List<ElementId>();
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
                // --- Step 1: Land Area Input and Crop Region Update ---
                LandInputWindow landWindow = new LandInputWindow();
                bool? landResult = landWindow.ShowDialog();
                if (landResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled land area input.");
                    return Result.Cancelled;
                }

                // Retrieve user-inputted land dimensions
                double userWidth = landWindow.InputWidth;
                double userHeight = landWindow.InputHeight;
                double userArea = landWindow.LandArea; // User input area (in sq ft)

                // Store values in global data
                GlobalData.landWidth = userWidth;
                GlobalData.landHeight = userHeight;

                // Retrieve the active view and its current crop box
                View activeView = doc.ActiveView;
                BoundingBoxXYZ cropBox = activeView.CropBox;

                // Calculate the center of the current crop box
                XYZ center = new XYZ(
                    (cropBox.Min.X + cropBox.Max.X) / 2.0,
                    (cropBox.Min.Y + cropBox.Max.Y) / 2.0,
                    cropBox.Min.Z);

                // Use actual width and height instead of a square calculation
                double cropWidth = userWidth;
                double cropHeight = userHeight;

                // Calculate new min and max points for the crop box
                XYZ newMin = new XYZ(center.X - (cropWidth / 2.0), center.Y - (cropHeight / 2.0), cropBox.Min.Z);
                XYZ newMax = new XYZ(center.X + (cropWidth / 2.0), center.Y + (cropHeight / 2.0), cropBox.Max.Z);

                // Update the crop region inside a transaction
                using (Transaction trans = new Transaction(doc, "Update Crop Region"))
                {
                    trans.Start();
                    activeView.CropBoxActive = true;
                    BoundingBoxXYZ newCropBox = activeView.CropBox;
                    newCropBox.Min = newMin;
                    newCropBox.Max = newMax;
                    activeView.CropBox = newCropBox;
                    trans.Commit();
                }

                double availableLayoutArea = userWidth * userHeight;
                GlobalData.LandArea = availableLayoutArea;

                // --- Step 1: Room Input and Room Instances Creation ---
                RoomInputWindow firstWindow = new RoomInputWindow();
                bool? firstResult = firstWindow.ShowDialog();
                if (firstResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled at the first window.");
                    return Result.Cancelled;
                }

                List<RoomTypeRow> userSelections = firstWindow.RoomTypes;
                List<RoomInstanceRow> instanceRows = new List<RoomInstanceRow>();

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
                            Area = 0.0 // Default area
                        };
                        instanceRows.Add(instance);
                    }
                }

                if (instanceRows.Count == 0)
                {
                    TaskDialog.Show("Info", "No rooms were requested.");
                    return Result.Cancelled;
                }

                // Open second window for room adjustments
                RoomInstancesWindow secondWindow = new RoomInstancesWindow(instanceRows);
                bool? secondResult = secondWindow.ShowDialog();
                if (secondResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled at the second window.");
                    return Result.Cancelled;
                }

                // Create room nodes (spaces) from user adjustments
                List<SpaceNode> spaces = new List<SpaceNode>();
                Random random = new Random();

                foreach (var inst in secondWindow.Instances)
                {
                    double area = inst.Area < 10.0 ? 10.0 : inst.Area;
                    View activeView1 = doc.ActiveView;
                    BoundingBoxXYZ viewBox1 = activeView1.CropBoxActive && activeView1.CropBox != null
                        ? activeView1.CropBox
                        : activeView1.get_BoundingBox(null);
                    double layoutWidth1 = viewBox1.Max.X - viewBox1.Min.X;
                    double layoutHeight1 = viewBox1.Max.Y - viewBox1.Min.Y;
                    XYZ position = new XYZ(viewBox1.Min.X + random.NextDouble() * layoutWidth1, viewBox1.Min.Y + random.NextDouble() * layoutHeight1, 0);
                    var node = new SpaceNode(inst.Name, inst.RoomType, area, position, inst.WpfColor);
                    spaces.Add(node);
                }

                activeView = doc.ActiveView;
                BoundingBoxXYZ viewBox = activeView.CropBoxActive && activeView.CropBox != null
                    ? activeView.CropBox
                    : activeView.get_BoundingBox(null);

                double totalRoomArea = 0.0;
                foreach (var space in spaces)
                {
                    totalRoomArea += space.Area;
                }
                GlobalData.TotalRoomArea = totalRoomArea;

                // --- Step 1: Adjacency, Connectivity, and Edge Weights ---
                PreferredAdjacencyWindow adjacencyWindow = new PreferredAdjacencyWindow(spaces);
                bool? result = adjacencyWindow.ShowDialog();
                if (result != true)
                {
                    TaskDialog.Show("Canceled", "User canceled at the preferred adjacency matrix window.");
                    return Result.Cancelled;
                }
                int[,] preferredAdjacency = adjacencyWindow.PreferredAdjacency;

                ConnectivityMatrixWindow connectivityWindow = new ConnectivityMatrixWindow(spaces);
                bool? connectivityResult = connectivityWindow.ShowDialog();
                if (connectivityResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled at the connectivity matrix window.");
                    return Result.Cancelled;
                }
                int[,] adjacencyMatrix = connectivityWindow.ConnectivityMatrix;

                EdgeWeightsWindow weightsWindow = new EdgeWeightsWindow(spaces, adjacencyMatrix);
                bool? weightResult = weightsWindow.ShowDialog();
                if (weightResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled the edge weights window.");
                    return Result.Cancelled;
                }
                double?[,] weightedAdjMatrix = weightsWindow.WeightedAdjacencyMatrix;

                // --- Step 1: Layout Calculation ---
                //ApplyForceDirectedLayout(spaces, preferredAdjacency, weightedAdjMatrix, viewBox);
                //SnapConnectedCircles(spaces, adjacencyMatrix);
                //ResolveCollisions(spaces);
                //CenterLayout(spaces, viewBox);



                List<List<SpaceNode>> layoutOptions = new List<List<SpaceNode>>();
                int maxLayouts = Math.Min(15, spaces.Count);

                // Ensure 'random' is declared only once in the correct scope.
                Random randomGenerator = new Random(); // Rename variable to avoid conflict

                for (int i = 0; i < maxLayouts; i++) // Generate up to 15 layouts
                {
                    List<SpaceNode> clonedSpaces = CloneSpaces(spaces); // Make a copy

                    // Apply random shifts before layout adjustment
                    foreach (var space in clonedSpaces)
                    {
                        double randomX = (randomGenerator.NextDouble() - 0.5) * 100; // Increase randomness
                        double randomY = (randomGenerator.NextDouble() - 0.5) * 100;
                        space.Position = new XYZ(space.Position.X + randomX, space.Position.Y + randomY, space.Position.Z);
                    }


                    ApplyForceDirectedLayout(clonedSpaces, preferredAdjacency, weightedAdjMatrix, viewBox);
                    SnapConnectedCircles(clonedSpaces, adjacencyMatrix);
                    ResolveCollisions(clonedSpaces);
                    CenterLayout(clonedSpaces, viewBox);


                    layoutOptions.Add(clonedSpaces);
                }

                // Show layout selection window
                LayoutSelectionWindow selectionWindow = new LayoutSelectionWindow(layoutOptions);
                bool? dialogResult = selectionWindow.ShowDialog();

                if (dialogResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled layout selection.");
                    return Result.Cancelled;
                }

                //  Get the selected layout
                List<SpaceNode> selectedSpace = selectionWindow.SelectedLayout;
                GlobalData.SavedSpaces = selectedSpace;














                // --- Step 1: Create Connection Lines ---
                using (Transaction tx = new Transaction(doc, "Connect Rooms"))
                {
                    tx.Start();
                    for (int i = 0; i < selectedSpace.Count; i++)  // use selectedSpace count
                    {
                        for (int j = i + 1; j < selectedSpace.Count; j++)
                        {
                            if (weightedAdjMatrix[i, j].HasValue)
                            {
                                // Use positions from selectedSpace instead of spaces
                                Line connectionLine = Line.CreateBound(selectedSpace[i].Position, selectedSpace[j].Position);
                                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, selectedSpace[i].Position);
                                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                                DetailCurve connectionDetail = doc.Create.NewDetailCurve(doc.ActiveView, connectionLine);

                                OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                                ogs.SetProjectionLineWeight(8);
                                doc.ActiveView.SetElementOverrides(connectionDetail.Id, ogs);

                                GlobalData.SavedConnectionLines.Add(connectionDetail.Id);
                                GlobalData.Step1Elements.Add(connectionDetail.Id);
                            }
                        }
                    }
                    tx.Commit();
                }

                // --- Step 1: Create Room Circles (and associated geometry) ---
                using (Transaction tx = new Transaction(doc, "Create Rooms"))
                {
                    tx.Start();
                    foreach (var space in selectedSpace)
                    {
                        CreateCircleNode(doc, space.Position, space.Area, space.WpfColor, space.Name, uidoc.ActiveView.Id);
                    }
                    tx.Commit();
                }

                TaskDialog.Show("Revit", $"Created {selectedSpace.Count} room(s) with connections.");
                GlobalData.SavedSpaces = selectedSpace;

                // --- Step 1 Complete: Show Output and Wait for User Confirmation ---
                TaskDialog step1Dialog = new TaskDialog("Step 1 Complete");
                step1Dialog.MainInstruction = "Step 1 output is displayed.";
                step1Dialog.MainContent = "Click CLOSE to clear the screen and proceed to Step 2.";
                step1Dialog.Show();

                // Clear Step 1 output by deleting all stored elements
                using (Transaction tx = new Transaction(doc, "Clear Step 1 Output"))
                {
                    tx.Start();
                    foreach (ElementId id in GlobalData.Step1Elements)
                    {
                        try
                        {
                            doc.Delete(id);
                        }
                        catch { /* Handle deletion exceptions if necessary */ }
                    }
                    tx.Commit();
                }
                GlobalData.Step1Elements.Clear();

                // --- Step 2: Module Input and New Output ---
                //STEP 2 STEP 2 STEP 2
                ModuleInputWindow inputWindow = new ModuleInputWindow();
                bool? inputResult = inputWindow.ShowDialog();
                if (inputResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled the module input.");
                    return Result.Cancelled;
                }

                double minWidth = inputWindow.MinWidth;
                GlobalData.moduleWidth = minWidth;
                double maxHeight = inputWindow.MaxHeight;

                ModuleTypesWindow typesWindow = new ModuleTypesWindow(minWidth, maxHeight);
                List<ModuleType> moduleTypes = typesWindow.ModuleTypes;

                bool arrangementCreated = false;
                ModuleArrangement arranger = null;
                while (!arrangementCreated)
                {
                    ModuleCombinationsWindow combWindow = new ModuleCombinationsWindow(moduleTypes, minWidth);
                    bool? combResult = combWindow.ShowDialog();
                    if (combResult != true)
                    {
                        TaskDialog.Show("Canceled", "User canceled the module combination selection.");
                        return Result.Cancelled;
                    }
                    string selectedCombination = combWindow.SelectedCombination;
                    TaskDialog.Show("Selected Combination", selectedCombination);





                    arranger = new ModuleArrangement();
                    ModuleArrangement previewArranger = new ModuleArrangement();
                    try
                    {

                        // --- Step 2 Preview: Display Module Arrangement (without grid) ---
                        List<ElementId> previewIds = previewArranger.DisplayModuleCombination(doc, selectedCombination, moduleTypes);

                        TaskDialog step2Dialog = new TaskDialog("Step 2 Complete");
                        step2Dialog.MainInstruction = "Step 2 output is displayed.";
                        step2Dialog.MainContent = "Click CLOSE to clear the screen and proceed to Step 3.";
                        step2Dialog.Show();

                        // --- Clear Step 2 Preview Output ---
                        using (Transaction tx = new Transaction(doc, "Clear Preview Output"))
                        {
                            tx.Start();
                            foreach (ElementId id in previewIds)
                            {
                                try
                                {
                                    doc.Delete(id);
                                }
                                catch
                                {
                                    // Handle deletion exceptions if necessary.
                                }
                            }
                            tx.Commit();
                        }



                        arranger.CreateSquareLikeArrangement(doc, selectedCombination, moduleTypes);
                        arrangementCreated = true;
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("Module doesn't fit"))
                        {
                            TaskDialog.Show("Error", "Module doesn't fit in row. Please select another combination.");
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

                // --- Step 2: Re-Output Saved Layout (Connection Lines and Room Circles) ---
                List<SpaceNode> savedSpaces = GlobalData.SavedSpaces;
                using (Transaction tx = new Transaction(doc, "Output Saved Layout"))
                {
                    tx.Start();

                    //// Recreate connection lines based on saved spaces
                    //for (int i = 0; i < savedSpaces.Count; i++)
                    //{
                    //    for (int j = i + 1; j < savedSpaces.Count; j++)
                    //    {
                    //        Line connectionLine = Line.CreateBound(savedSpaces[i].Position, savedSpaces[j].Position);
                    //        Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, savedSpaces[i].Position);
                    //        SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                    //        DetailCurve connectionDetail = doc.Create.NewDetailCurve(doc.ActiveView, connectionLine);

                    //        OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                    //        ogs.SetProjectionLineWeight(8);
                    //        doc.ActiveView.SetElementOverrides(connectionDetail.Id, ogs);
                    //    }
                    //}

                    XYZ overallBoundaryCenter = arranger.OverallCenter;
                    CenterLayoutOnOverallBoundary(savedSpaces, overallBoundaryCenter);


                    // Create circles for each saved space
                    foreach (var space in savedSpaces)
                    {
                        //STEP 2 CREATE CIRCLE NODE OUTPUT
                        CreateCircleNode(doc, space.Position, space.Area, space.WpfColor, space.Name, uidoc.ActiveView.Id);
                    }

                    tx.Commit();
                }
                // Display a dialog to let the user see the output before proceeding
                // Let the user see the Step 2 output before proceeding.
                TaskDialog outputDialog = new TaskDialog("Output Displayed");
                outputDialog.MainInstruction = "The output is now displayed.";
                outputDialog.MainContent = "Click OK to proceed to the next output.";
                outputDialog.Show();

                // Clear the output of CreateCircleNode for Step 2.
                // Clear the output of CreateCircleNode for Step 2.
                using (Transaction tx = new Transaction(doc, "Clear Step 2 Output"))
                {
                    tx.Start();
                    foreach (ElementId id in GlobalData.Step1Elements)
                    {
                        try
                        {
                            doc.Delete(id);
                        }
                        catch { /* Handle deletion exceptions if necessary */ }
                    }
                    tx.Commit();
                }
                GlobalData.Step1Elements.Clear();





                // --- STEP 3: Create Trimmed Square Arrangement (SquareArrangementOnly) ---
                List<XYZ[]> roomSquares = new List<XYZ[]>();
                foreach (var space in GlobalData.SavedSpaces)
                {
                    // Calculate a radius from the area (assuming the room circle was used earlier)
                    double radius = Math.Sqrt(space.Area / Math.PI);
                    // Define square corners (clockwise order) based on the room center.
                    XYZ pt1 = new XYZ(space.Position.X + radius, space.Position.Y + radius, space.Position.Z);
                    XYZ pt2 = new XYZ(space.Position.X - radius, space.Position.Y + radius, space.Position.Z);
                    XYZ pt3 = new XYZ(space.Position.X - radius, space.Position.Y - radius, space.Position.Z);
                    XYZ pt4 = new XYZ(space.Position.X + radius, space.Position.Y - radius, space.Position.Z);
                    roomSquares.Add(new XYZ[] { pt1, pt2, pt3, pt4 });
                }


                // Ask the ModuleArrangement for its bounding rectangle
                double gridMinX, gridMinY, gridMaxX, gridMaxY;
                arranger.GetArrangementBounds(out gridMinX, out gridMinY, out gridMaxX, out gridMaxY);


                // Create the trimmed square arrangement using the SquareArrangementOnly (or TrimCircleSquare) class.
                TrimCircleSquare trimmer = new TrimCircleSquare();
                using (Transaction tx = new Transaction(doc, "Trim Squares"))
                {
                    tx.Start();
                    trimmer.CreateTrimmedSquares(doc, roomSquares, gridMinX, gridMinY, gridMaxX, gridMaxY);

                    tx.Commit();
                }


                // Display the total trimmed area calculated by the new class.
                TaskDialog.Show("Square Arrangement Only", $"Total trimmed area: {trimmer.TotalTrimmedArea}");















                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", ex.Message);
                return Result.Failed;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Method: CreateCircleNode
        ////////////////////////////////////////////////////////////////////////////////
        private void CreateCircleNode(Document doc, XYZ position, double area, System.Windows.Media.Color wpfColor, string roomName, ElementId viewId)
        {
            double areaFt2 = area;
            double radius = Math.Sqrt(areaFt2 / Math.PI);
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, position);
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

            // Create the circle as a detail curve.
            Curve fullCircle = Ellipse.CreateCurve(position, radius, radius, XYZ.BasisX, XYZ.BasisY, 0, 2 * Math.PI);
            Autodesk.Revit.DB.Color revitColor = new Autodesk.Revit.DB.Color(wpfColor.R, wpfColor.G, wpfColor.B);
            GraphicsStyle gs = GetOrCreateLineStyle(doc, $"RoomStyle_{wpfColor}", revitColor);
            DetailCurve circleCurve = doc.Create.NewDetailCurve(doc.ActiveView, fullCircle);
            circleCurve.LineStyle = gs;
            GlobalData.Step1Elements.Add(circleCurve.Id);

            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineWeight(9);
            doc.ActiveView.SetElementOverrides(circleCurve.Id, ogs);

            // Create a square surrounding the circle.
            double d = radius;
            XYZ pt1 = new XYZ(position.X + d, position.Y + d, position.Z);
            XYZ pt2 = new XYZ(position.X - d, position.Y + d, position.Z);
            XYZ pt3 = new XYZ(position.X - d, position.Y - d, position.Z);
            XYZ pt4 = new XYZ(position.X + d, position.Y - d, position.Z);

            GraphicsStyle squareStyle = GetOrCreateLineStyle(doc, "SquareThinBlack", new Autodesk.Revit.DB.Color(0, 0, 0));
            DetailCurve squareCurve1 = doc.Create.NewDetailCurve(doc.ActiveView, Line.CreateBound(pt1, pt2));
            squareCurve1.LineStyle = squareStyle;
            GlobalData.Step1Elements.Add(squareCurve1.Id);
            DetailCurve squareCurve2 = doc.Create.NewDetailCurve(doc.ActiveView, Line.CreateBound(pt2, pt3));
            squareCurve2.LineStyle = squareStyle;
            GlobalData.Step1Elements.Add(squareCurve2.Id);
            DetailCurve squareCurve3 = doc.Create.NewDetailCurve(doc.ActiveView, Line.CreateBound(pt3, pt4));
            squareCurve3.LineStyle = squareStyle;
            GlobalData.Step1Elements.Add(squareCurve3.Id);
            DetailCurve squareCurve4 = doc.Create.NewDetailCurve(doc.ActiveView, Line.CreateBound(pt4, pt1));
            squareCurve4.LineStyle = squareStyle;
            GlobalData.Step1Elements.Add(squareCurve4.Id);

            // Create a text note for unique room types.
            string roomType = roomName.Split(' ')[0];
            if (!GlobalData.UniqueRoomTypesDisplayed.Contains(roomType))
            {
                GlobalData.UniqueRoomTypesDisplayed.Add(roomType);
                View currentView = doc.GetElement(viewId) as View;
                BoundingBoxXYZ cropBox = currentView.CropBox;
                double offsetX = 5.0;
                double noteHeightSpacing = 10.0;
                double noteX = cropBox.Max.X + offsetX;
                double noteY = cropBox.Max.Y - (GlobalData.TextNoteUniqueCounter * noteHeightSpacing);
                XYZ notePosition = new XYZ(noteX, noteY, cropBox.Min.Z);

                FilteredElementCollector collector = new FilteredElementCollector(doc)
                                                        .OfClass(typeof(TextNoteType));
                TextNoteType textNoteType = collector.FirstElement() as TextNoteType;
                if (textNoteType != null)
                {
                    TextNote textNote = TextNote.Create(doc, viewId, notePosition, roomType, textNoteType.Id);
                    OverrideGraphicSettings textOgs = new OverrideGraphicSettings();
                    textOgs.SetProjectionLineColor(revitColor);
                    doc.ActiveView.SetElementOverrides(textNote.Id, textOgs);
                    GlobalData.Step1Elements.Add(textNote.Id);
                }
                GlobalData.TextNoteUniqueCounter++;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Method: GetOrCreateLineStyle
        ////////////////////////////////////////////////////////////////////////////////
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

        // Force-directed layout constants and methods follow...
        private const int ITERATIONS = 100;
        private const double PREFERRED_ADJ_FACTOR = 10.0;
        private const double SPRING_CONSTANT = 0.5;
        private const double REPULSION_CONSTANT = 5.0;
        private const double DAMPING = 0.85;
        private const double MOVEMENT_THRESHOLD = 0.1;
        private const bool ENABLE_ADAPTIVE_DAMPING = true;

        private void ApplyForceDirectedLayout(List<SpaceNode> spaces,
                                      int[,] preferredAdjMatrix,
                                      double?[,] weightedAdjMatrix,
                                      BoundingBoxXYZ viewBox)
        {
            for (int iter = 0; iter < ITERATIONS; iter++)
            {
                double currentDamping = DAMPING;
                if (ENABLE_ADAPTIVE_DAMPING)
                {
                    currentDamping = DAMPING - (DAMPING / 2.0) * (iter / (double)ITERATIONS);
                }
                XYZ[] forces = new XYZ[spaces.Count];
                for (int i = 0; i < forces.Length; i++)
                    forces[i] = XYZ.Zero;
                for (int i = 0; i < spaces.Count; i++)
                {
                    for (int j = i + 1; j < spaces.Count; j++)
                    {
                        XYZ posI = spaces[i].Position;
                        XYZ posJ = spaces[j].Position;
                        XYZ delta = posJ - posI;
                        double distance = delta.GetLength();
                        if (distance < 1e-6) distance = 1e-6;
                        double repForce = REPULSION_CONSTANT / (distance * distance);
                        XYZ repulsion = repForce * delta.Normalize();
                        double weight = 0.0;
                        if (weightedAdjMatrix[i, j].HasValue && weightedAdjMatrix[i, j].Value > 0)
                        {
                            weight = weightedAdjMatrix[i, j].Value;
                        }
                        if (preferredAdjMatrix[i, j] == 1)
                        {
                            weight = (weight == 0.0) ? 1.0 : weight * PREFERRED_ADJ_FACTOR;
                        }
                        double desiredDistance = 1.0;
                        if (preferredAdjMatrix[i, j] == 1)
                        {
                            desiredDistance = spaces[i].Radius + spaces[j].Radius;
                        }
                        double attrForce = 0.0;
                        if (weight > 0)
                        {
                            attrForce = SPRING_CONSTANT * weight * (distance - desiredDistance);
                        }
                        XYZ attraction = -attrForce * delta.Normalize();
                        XYZ forceIJ = repulsion + attraction;
                        forces[i] -= forceIJ;
                        forces[j] += forceIJ;
                    }
                }
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
                double averageMovement = totalMovement / spaces.Count;
                if (averageMovement < MOVEMENT_THRESHOLD)
                {
                    break;
                }
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

        private double GetCircleRadius(double area)
        {
            return Math.Sqrt(area / Math.PI);
        }

        private void SnapConnectedCircles(List<SpaceNode> spaces, int[,] connectivityMatrix)
        {
            const double tolerance = 0.001;
            bool adjusted = true;
            int iterations = 0;
            int maxIterations = 10;
            while (adjusted && iterations < maxIterations)
            {
                adjusted = false;
                for (int i = 0; i < spaces.Count; i++)
                {
                    for (int j = i + 1; j < spaces.Count; j++)
                    {
                        if (connectivityMatrix[i, j] == 1)
                        {
                            double radiusI = GetCircleRadius(spaces[i].Area);
                            double radiusJ = GetCircleRadius(spaces[j].Area);
                            double desiredDistance = radiusI + radiusJ;
                            XYZ delta = spaces[j].Position - spaces[i].Position;
                            double currentDistance = delta.GetLength();
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
            XYZ layoutCenter = new XYZ((layoutMin.X + layoutMax.X) / 2.0, (layoutMin.Y + layoutMax.Y) / 2.0, 0);
            XYZ viewCenter = new XYZ((viewBox.Min.X + viewBox.Max.X) / 2.0, (viewBox.Min.Y + viewBox.Max.Y) / 2.0, 0);
            XYZ offset = viewCenter - layoutCenter;
            foreach (var node in spaces)
            {
                node.Position += offset;
            }
        }



        private void CenterLayoutOnOverallBoundary(List<SpaceNode> spaces, XYZ overallBoundaryCenter)
        {
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
            XYZ layoutCenter = new XYZ((layoutMin.X + layoutMax.X) / 2.0,
                                       (layoutMin.Y + layoutMax.Y) / 2.0, 0);
            XYZ offset = overallBoundaryCenter - layoutCenter;
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
                var newNode = new SpaceNode(
                    node.Name,
                    node.Function,
                    node.Area,
                    new XYZ(node.Position.X, node.Position.Y, node.Position.Z),  // 🚨 Position must be cloned properly!
                    node.WpfColor)
                {
                    Radius = node.Radius
                };
                cloned.Add(newNode);
            }
            return cloned;
        }


    }
}