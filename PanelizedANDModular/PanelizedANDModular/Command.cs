#region Namespaces

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
using System.Diagnostics;
using System.Windows.Interop;
using static PanelizedAndModularFinal.GridTrimmer;
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

   
    public static List<ElementId> Step1Elements { get; set; } = new List<ElementId>();
}


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
                while (true)
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

                    List<RoomTypeRow> userSelections = null;
                    List<RoomInstanceRow> instanceRows = null;

                    while (true)
                    {
                        double availableLayoutArea = userWidth * userHeight;
                        GlobalData.LandArea = availableLayoutArea;
                        // --- Step 1: Room Input and Room Instances Creation ---
                        RoomInputWindow firstWindow = userSelections == null
                            ? new RoomInputWindow()
                            : new RoomInputWindow(userSelections);

                        bool? firstResult = firstWindow.ShowDialog();
                        if (firstResult != true)
                        {
                            if (firstWindow.UserWentBack)
                            {
                                return Execute(commandData, ref message, elements); // re-run the command
                            }
                            TaskDialog.Show("Canceled", "User canceled at the first window.");
                            return Result.Cancelled;
                        }

                        userSelections = firstWindow.RoomTypes;
                        instanceRows = new List<RoomInstanceRow>();

                        foreach (var row in userSelections)
                        {
                            if (row.Quantity <= 0) continue;
                            for (int i = 0; i < row.Quantity; i++)
                            {
                                instanceRows.Add(new RoomInstanceRow
                                {
                                    RoomType = row.Name,
                                    Name = $"{row.Name} {i + 1}",
                                    WpfColor = row.Color,
                                    Area = 0.0
                                });
                            }
                        }

                        if (instanceRows.Count == 0)
                        {
                            TaskDialog.Show("Info", "No rooms were requested.");
                            return Result.Cancelled;
                        }
                        while (true)
                        {

                            // Open second window for room adjustments
                            RoomInstancesWindow secondWindow = new RoomInstancesWindow(instanceRows);
                            bool? secondResult = secondWindow.ShowDialog();
                            if (secondResult != true)
                            {
                                if (secondWindow.UserWentBack)
                                    break; // Go back to RoomInputWindow
                                else
                                    TaskDialog.Show("Canceled", "User canceled at the second window.");
                                return Result.Cancelled;
                            }

                            // Create room nodes (spaces) from user adjustments
                            List<SpaceNode> spaces = new List<SpaceNode>();
                            Random random = new Random();

                            foreach (var inst in secondWindow.Instances)
                            {
                                double area = inst.Area < 10.0 ? 25.0 : inst.Area;
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
                            while (true)
                            {
                                // --- Step 1: Adjacency, Connectivity, and Edge Weights ---
                                PreferredAdjacencyWindow adjacencyWindow = new PreferredAdjacencyWindow(spaces);
                                bool? result = adjacencyWindow.ShowDialog();
                                if (result != true)
                                {
                                    if (adjacencyWindow.UserWentBack)
                                        break;
                                    else
                                        TaskDialog.Show("Canceled", "User canceled at the preferred adjacency matrix window.");
                                    return Result.Cancelled;
                                }
                                int[,] preferredAdjacency = adjacencyWindow.PreferredAdjacency;

                                while (true)
                                {
                                    ConnectivityMatrixWindow connectivityWindow = new ConnectivityMatrixWindow(spaces);
                                    bool? connectivityResult = connectivityWindow.ShowDialog();
                                    if (connectivityResult != true)
                                    {
                                        if (connectivityWindow.UserWentBack)
                                            break;
                                        else
                                            TaskDialog.Show("Canceled", "User canceled at the connectivity matrix window.");
                                        return Result.Cancelled;
                                    }
                                    int[,] adjacencyMatrix = connectivityWindow.ConnectivityMatrix;

                                    while (true)
                                    {
                                        EdgeWeightsWindow weightsWindow = new EdgeWeightsWindow(spaces, adjacencyMatrix);
                                        bool? weightResult = weightsWindow.ShowDialog();
                                        if (weightResult != true)
                                        {
                                            if (weightsWindow.UserWentBack)
                                                break;
                                            else
                                                TaskDialog.Show("Canceled", "User canceled the edge weights window.");
                                            return Result.Cancelled;
                                        }
                                        double?[,] weightedAdjMatrix = weightsWindow.WeightedAdjacencyMatrix;

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
                                            ResolveBoundaryViolations(clonedSpaces, viewBox);
                                            ResolveCollisionsEnsureAllAdjacent(clonedSpaces, viewBox);
                                            CenterLayout(clonedSpaces, viewBox);


                                            layoutOptions.Add(clonedSpaces);
                                        }

                                        while (true)
                                        {
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

                                            TaskDialog.Show("Revit", $"Created {spaces.Count} room(s) with connections.");
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

                                            // inside your Step 2: Module Input and New Output block

                                            bool arrangementCreated = false;
                                            ModuleArrangement arrangement = null;
                                            ModuleArrangementResult chosen = null;

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

                                                BoundingBoxXYZ updatedCrop = activeView.CropBox;
                                                double updatedWidth = updatedCrop.Max.X - updatedCrop.Min.X;
                                                double updatedHeight = updatedCrop.Max.Y - updatedCrop.Min.Y;

                                                GlobalData.landWidth = updatedWidth;
                                                GlobalData.landHeight = updatedHeight;

                                                using (var tx = new Transaction(doc, "Hide Crop Region"))
                                                {
                                                    tx.Start();
                                                    activeView.CropBoxActive = false;
                                                    activeView.CropBoxVisible = false;
                                                    tx.Commit();
                                                }

                                                var corners = new List<XYZ>
    {
        new XYZ(updatedCrop.Min.X, updatedCrop.Min.Y, 0),
        new XYZ(updatedCrop.Max.X, updatedCrop.Min.Y, 0),
        new XYZ(updatedCrop.Max.X, updatedCrop.Max.Y, 0),
        new XYZ(updatedCrop.Min.X, updatedCrop.Max.Y, 0),
        new XYZ(updatedCrop.Min.X, updatedCrop.Min.Y, 0),
    };

                                                using (var tx2 = new Transaction(doc, "Draw Land Boundary"))
                                                {
                                                    tx2.Start();
                                                    foreach (var pair in corners.Zip(corners.Skip(1), Tuple.Create))
                                                    {
                                                        Line edge = Line.CreateBound(pair.Item1, pair.Item2);
                                                        doc.Create.NewDetailCurve(activeView, edge);
                                                    }
                                                    tx2.Commit();
                                                }

                                                arrangement = new ModuleArrangement(moduleTypes, selectedCombination, updatedCrop);
                                                List<ModuleArrangementResult> uniqueArrangements = arrangement.GetValidArrangements();

                                                if (uniqueArrangements.Count == 0)
                                                {
                                                    string message2 =
                                                        $"No valid arrangements found. Module width or height may exceed land dimensions.\n" +
                                                        $"Land Width: {GlobalData.landWidth:F2}, Land Height: {GlobalData.landHeight:F2}\n" +
                                                        $"Module Width: {minWidth:F2}, Max Height: {maxHeight:F2}\n\n" +
                                                        "Please try another combination.";

                                                    TaskDialog.Show("Arrangement Error", message2);
                                                    continue; // LOOP TO LET USER PICK AGAIN
                                                }

                                                arrangement.DisplayScenarioSummary(uniqueArrangements);
                                                arrangement.DisplayUniqueCount(uniqueArrangements);

                                                var pickWin = new ArrangementSelectionWindow(uniqueArrangements);
                                                var helper = new WindowInteropHelper(pickWin);
                                                helper.Owner = Process.GetCurrentProcess().MainWindowHandle;

                                                bool? picked = pickWin.ShowDialog();
                                                if (picked != true)
                                                {
                                                    return Result.Cancelled;
                                                }

                                                chosen = pickWin.SelectedArrangement;
                                                int moduleCount = chosen.PlacedModules.Count;
                                                List<ElementId> drawnIds = arrangement.DrawArrangement(doc, chosen);

                                                arrangementCreated = true;
                                            }







                                            // --- Step 2: Re-Output Saved Layout (Connection Lines and Room Circles) ---
                                            List<SpaceNode> savedSpaces = GlobalData.SavedSpaces;
                                            using (Transaction tx = new Transaction(doc, "Output Saved Layout"))
                                            {
                                                tx.Start();

                                                //XYZ overallBoundaryCenter = arranger.OverallCenter;
                                                //CenterLayoutOnOverallBoundary(savedSpaces, overallBoundaryCenter);


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

                                       


                                            // Overlay the square grid inside each module and get cell areas
                                            List<double> cellAreas;
                                            List<ElementId> gridLines = arrangement.DrawModuleGrids(doc, chosen, out cellAreas);





                                            // 8) Inform the user
                                            TaskDialog.Show(
                                                "Grid Overlay",
                                                $"Each cell area: {cellAreas.First():F2} sq units."
                                            );






                                            /////////////////////////////////////////////////////////////////////
                                            //DISPLAY COLORED SQUARE////////////////////////////////////////////////////
                                            /////////////////////////////////////////////////////////////////



                                            var fullGridIds = new List<ElementId>();

                                            // 1. Draw perfectly‐fitting 3×3 colored, semi‐transparent grid inside each saved space’s square
                                            using (var trans = new Transaction(doc, "Draw Perfect 3x3 Grids"))
                                            {
                                                trans.Start();

                                                // 0a) Grab one FilledRegionType for the regions
                                                var regionType = new FilteredElementCollector(doc)
                                                    .OfClass(typeof(FilledRegionType))
                                                    .Cast<FilledRegionType>()
                                                    .First();

                                                // 0b) Grab a solid drafting fill pattern for the overrides
                                                var fillPattern = new FilteredElementCollector(doc)
                                                    .OfClass(typeof(FillPatternElement))
                                                    .Cast<FillPatternElement>()
                                                    .First(fp =>
                                                        fp.GetFillPattern().IsSolidFill &&
                                                        fp.GetFillPattern().Target == FillPatternTarget.Drafting
                                                    );

                                                var view = doc.ActiveView;

                                                foreach (var space in GlobalData.SavedSpaces)
                                                {
                                                    // 1) Compute the square bounds
                                                    double radius = Math.Sqrt(space.Area / Math.PI);
                                                    double cx = space.Position.X;
                                                    double cy = space.Position.Y;
                                                    double z = space.Position.Z;
                                                    double side = 2 * radius;
                                                    double minX = cx - radius;
                                                    double minY = cy - radius;

                                                    // 2) Each square is broken into exactly 3×3 cells
                                                    int nCols = 3;
                                                    int nRows = 3;
                                                    double cellSize = side / 3.0;    // fits perfectly

                                                    // 3) Prepare sketch plane at bottom‐left corner
                                                    var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(minX, minY, z));
                                                    var sp = SketchPlane.Create(doc, plane);

                                                    // 4) Build one OverrideGraphicSettings for this space
                                                    var ogs = new OverrideGraphicSettings()
                                                        // fill color & transparency
                                                        .SetSurfaceForegroundPatternColor(new Autodesk.Revit.DB.Color(
                                                            space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                                                        .SetSurfaceBackgroundPatternColor(new Autodesk.Revit.DB.Color(
                                                            space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                                                        .SetSurfaceForegroundPatternId(fillPattern.Id)
                                                        .SetSurfaceBackgroundPatternId(fillPattern.Id)
                                                        .SetSurfaceTransparency(50)  // 50% see‐through
                                                                                     // outline color & weight
                                                        .SetProjectionLineColor(new Autodesk.Revit.DB.Color(
                                                            space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                                                        .SetProjectionLineWeight(5);

                                                    // 5) Tile and draw each of the 3×3 cells
                                                    for (int i = 0; i < nCols; i++)
                                                    {
                                                        for (int j = 0; j < nRows; j++)
                                                        {
                                                            double x0 = minX + i * cellSize;
                                                            double y0 = minY + j * cellSize;
                                                            double x1 = x0 + cellSize;
                                                            double y1 = y0 + cellSize;

                                                            // build a CurveLoop of 4 edges
                                                            var loop = new CurveLoop();
                                                            loop.Append(Line.CreateBound(new XYZ(x0, y0, z), new XYZ(x1, y0, z)));
                                                            loop.Append(Line.CreateBound(new XYZ(x1, y0, z), new XYZ(x1, y1, z)));
                                                            loop.Append(Line.CreateBound(new XYZ(x1, y1, z), new XYZ(x0, y1, z)));
                                                            loop.Append(Line.CreateBound(new XYZ(x0, y1, z), new XYZ(x0, y0, z)));

                                                            // create filled region using regionType
                                                            var region = FilledRegion.Create(
                                                                doc,
                                                                regionType.Id,
                                                                view.Id,
                                                                new List<CurveLoop> { loop }
                                                            );


                                                       
                                                            fullGridIds.Add(region.Id);
                                                            view.SetElementOverrides(region.Id, ogs);


                                                            // apply semi‐transparent color & outline
                                                            view.SetElementOverrides(region.Id, ogs);

                                                            // record each cell’s area if desired
                                                            space.SquareArea = cellSize * cellSize;
                                                        }
                                                    }
                                                }

                                                trans.Commit();
                                            }

                                            // 2) Let the user see it, then remove
                                            TaskDialog.Show(
                                                "Step 1 Complete",
                                                "The full 3×3 colored grids are displayed.\nClick OK to trim off excess."
                                            );

                                            if (fullGridIds.Any())
                                            {
                                                using (var tx = new Transaction(doc, "Clear Full Grids"))
                                                {
                                                    tx.Start();
                                                    doc.Delete(fullGridIds);
                                                    tx.Commit();
                                                }
                                            }







                                            //////////////////////////////////////////////////////////////////////////////////
                                            //TRIMMING STEP !!!!
                                            /////////////////////////////////////////////////////////////////////////////////
                                            ///






                                            var gridTrimmer = new GridTrimmer();
                                            List<GridTrimmer.TrimResult> trims;
                                            var trimmedIds = gridTrimmer.DrawTrimmedGrids(
                                                doc,
                                                chosen,                      //  chosen ModuleArrangementResult
                                                GlobalData.SavedSpaces,
                                                out trims
                                            );

                                            // 4) Show both the trimmed cells and the per‐space totals
                                            var lines = new List<string>();

                                            // 4a) List each trimmed cell
                                            lines.Add("Trimmed Cells:");
                                            foreach (var t in trims)
                                            {
                                                lines.Add(
                                                  $"  {t.Space.Name} – cell #{t.CellIndex + 1}: " +
                                                  $"{t.TrimmedArea:F2} sq units trimmed"
                                                );
                                            }




                                            // 4b) Blank separator
                                            lines.Add("");
                                            // 4c) Totals per space
                                            lines.Add("Total Trimmed per Space:");
                                            double grandTotal = 0;
                                            foreach (var space in GlobalData.SavedSpaces)
                                            {
                                                double trimmed = space.SquareTrimmedArea;
                                                lines.Add($"  {space.Name}: {trimmed:F2} sq units");
                                                grandTotal += trimmed;

                                            
                                            }

                                            // 4d) Grand total
                                            lines.Add("");
                                            lines.Add($"Grand Total Trimmed: {grandTotal:F2} sq units");

                                            // Finally display
                                            TaskDialog.Show(
                                                "Trim Results",
                                                string.Join("\n", lines)
                                            );


                                            // Optional: let the user review then clear the trimmed regions before assignment
                                            TaskDialog.Show("Trim Complete", "The trimmed squares are drawn. Click OK to assign cells.");
                                            using (var tx = new Transaction(doc, "Clear Trimmed Regions"))
                                            {
                                                tx.Start();
                                                doc.Delete(trimmedIds);
                                                tx.Commit();
                                            }



                                            /////////////////////////////////////////////////////////////////////////
                                            //ASSIGNMENT OF CELLS STEP BELOW////////////////////////////////////////
                                            ////////////////////////////////////////////////////////////////////////////









                                            // 1) Draw your module grid cells as before
                                            var moduleCells = chosen.GridCells;

                                            // 2) Phase 1: fill each space’s unique cells
                                            var filler = new CellAssigner(doc, doc.ActiveView);
                                            foreach (var space in GlobalData.SavedSpaces)
                                            {
                                                FillResult result1 = filler.FillOverlappingCells(moduleCells, space);

                                                TaskDialog.Show(
                                                    $"Filled “{space.Name}”",
                                                    $"Cells colored: {result1.RegionIds.Count}\n" +
                                                    $"Total overlap area: {result1.TotalOverlapArea:F2} ft²\n" +
                                                    $"Extra allocated: {result1.TotalExtraArea:F2} ft²\n" +
                                                    $"Remaining trimmed area: {space.SquareTrimmedArea:F2} ft²"
                                                );
                                            }

                                            // 3) Phase 2: handle any cells shared by ≥2 spaces
                                            List<ElementId> contestedRegions = filler.ResolveContestedCells(moduleCells);

                                            // (optional) report how many contested cells you ultimately colored
                                            TaskDialog.Show(
                                                "Contested Cells Resolved",
                                                $"Cells filled in Phase 2: {contestedRegions.Count}"
                                            );
























                                            //// 7) Run the CellAssigner
                                            //var assigner = new CellAssigner1(
                                            //    moduleCells,
                                            //    cellAreas.ToArray(),
                                            //    trims,
                                            //    GlobalData.SavedSpaces
                                            //);





                                            //assigner.DrawAllAssignments(doc);


















                                            /////////////////////////////////////////////////////////////////////////////////////////////////////////
                                            //SPACE PRIORITY/////////////////////////////////////////////////////////////////////////////////////////
                                            /////////////////////////////////////////////////////////////////////////////////////////////////////////

                                            //// Now, open the space priority window to let the user assign raw priority values.

                                            //SpacePriorityWindow priorityWindow = new SpacePriorityWindow(GlobalData.SavedSpaces);
                                            //bool? priorityResult = priorityWindow.ShowDialog();

                                            //if (priorityResult != true)
                                            //{
                                            //    TaskDialog.Show("Canceled", "User canceled at the priority window.");
                                            //    return Result.Cancelled;
                                            //}

                                            //// At this point, each SpaceNode's Priority property has been normalized.
                                            //// You can now access these values for subsequent operations.


                                            return Result.Succeeded;
                                        }
                                        return Result.Cancelled;
                                    }
                                }
                            }
                        }
                    }
                }
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
            ogs.SetProjectionLineWeight(7);
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

        // Force-directed layout constants and methods...
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
                double currentDamping = ENABLE_ADAPTIVE_DAMPING
                    ? DAMPING - (DAMPING / 2.0) * (iter / (double)ITERATIONS)
                    : DAMPING;

                XYZ[] forces = new XYZ[spaces.Count];
                for (int i = 0; i < forces.Length; i++)
                    forces[i] = XYZ.Zero;

                // Calculate forces between nodes.
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
                        double desiredDistance = (preferredAdjMatrix[i, j] == 1)
                            ? spaces[i].Radius + spaces[j].Radius
                            : 1.0;
                        double attrForce = (weight > 0)
                            ? SPRING_CONSTANT * weight * (distance - desiredDistance)
                            : 0.0;
                        XYZ attraction = -attrForce * delta.Normalize();
                        XYZ forceIJ = repulsion + attraction;
                        forces[i] -= forceIJ;
                        forces[j] += forceIJ;
                    }
                }

                // Apply forces and update positions.
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

                // Resolve collisions between nodes.
                ResolveCollisions(spaces);
                // Resolve any nodes that exceed the viewBox boundaries.
                ResolveBoundaryViolations(spaces, viewBox);
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

        private void ResolveBoundaryViolations(List<SpaceNode> spaces, BoundingBoxXYZ viewBox)
        {
            const int maxAttempts = 36; // 36 attempts => 10° steps (360° total)
            const double angleStep = Math.PI / 18.0; // 10 degrees in radians

            foreach (SpaceNode node in spaces)
            {
                double radius = GetCircleRadius(node.Area);
                // Check if the node's circle goes out of the view box.
                if (node.Position.X - radius < viewBox.Min.X ||
                    node.Position.X + radius > viewBox.Max.X ||
                    node.Position.Y - radius < viewBox.Min.Y ||
                    node.Position.Y + radius > viewBox.Max.Y)
                {
                    // Find the closest node.
                    SpaceNode closest = null;
                    double minDist = double.MaxValue;
                    foreach (SpaceNode other in spaces)
                    {
                        if (other == node) continue;
                        double d = (node.Position - other.Position).GetLength();
                        if (d < minDist)
                        {
                            minDist = d;
                            closest = other;
                        }
                    }

                    if (closest != null)
                    {
                        double closestRadius = GetCircleRadius(closest.Area);
                        // Start with the direction from the closest node to the current node.
                        XYZ direction = node.Position - closest.Position;
                        if (direction.GetLength() < 1e-6)
                            direction = new XYZ(1, 0, 0);
                        direction = direction.Normalize();

                        bool found = false;
                        XYZ newPos = node.Position;
                        for (int attempt = 0; attempt < maxAttempts; attempt++)
                        {
                            // Calculate new position so that circles just touch.
                            newPos = closest.Position + direction * (closestRadius + radius);
                            // Clamp new position to ensure the full circle remains within viewBox.
                            double clampedX = Math.Max(viewBox.Min.X + radius, Math.Min(newPos.X, viewBox.Max.X - radius));
                            double clampedY = Math.Max(viewBox.Min.Y + radius, Math.Min(newPos.Y, viewBox.Max.Y - radius));
                            newPos = new XYZ(clampedX, clampedY, node.Position.Z);

                            // Check for overlaps with all other nodes.
                            bool overlap = false;
                            foreach (SpaceNode other in spaces)
                            {
                                if (other == node)
                                    continue;
                                double otherRadius = GetCircleRadius(other.Area);
                                if ((newPos - other.Position).GetLength() < (radius + otherRadius))
                                {
                                    overlap = true;
                                    break;
                                }
                            }
                            if (!overlap)
                            {
                                found = true;
                                break;
                            }
                            // Rotate the direction slightly and try again.
                            direction = Rotate(direction, angleStep);
                        }
                        node.Position = newPos;
                    }
                    else
                    {
                        // Fallback: simply clamp the current position.
                        double clampedX = Math.Max(viewBox.Min.X + radius, Math.Min(node.Position.X, viewBox.Max.X - radius));
                        double clampedY = Math.Max(viewBox.Min.Y + radius, Math.Min(node.Position.Y, viewBox.Max.Y - radius));
                        node.Position = new XYZ(clampedX, clampedY, node.Position.Z);
                    }
                }
            }
        }

        /// <summary>
        /// Rotates the given 2D vector (ignoring the Z component) by the given angle (in radians).
        /// </summary>
        private XYZ Rotate(XYZ vector, double angle)
        {
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            double x = vector.X * cos - vector.Y * sin;
            double y = vector.X * sin + vector.Y * cos;
            return new XYZ(x, y, vector.Z);
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
                    new XYZ(node.Position.X, node.Position.Y, node.Position.Z),
                    node.WpfColor)
                {
                    Radius = node.Radius
                };
                cloned.Add(newNode);
            }
            return cloned;
        }









        private void ResolveCollisionsEnsureAllAdjacent(List<SpaceNode> spaces, BoundingBoxXYZ viewBox)
        {
            const double epsilon = 0.001;
            const int maxIterations = 50;

            // 1) Standard collision resolution loop to remove overlaps.
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

                            spaces[i].Position = ClampToViewBox(spaces[i].Position, radiusI, viewBox);
                            spaces[j].Position = ClampToViewBox(spaces[j].Position, radiusJ, viewBox);
                        }
                    }
                }
            }
            while (hasOverlap);

            int iteration = 0;
            // 2) Ensure every circle has at least one adjacency.
            while (!AllCirclesHaveAdjacency(spaces, epsilon) && iteration < maxIterations)
            {
                for (int i = 0; i < spaces.Count; i++)
                {
                    if (!CircleHasAdjacency(spaces, i, epsilon))
                    {
                        ForceAdjacencyWithClosest(spaces, i, viewBox);
                    }
                }
                // Re-run collision resolution
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

                                spaces[i].Position = ClampToViewBox(spaces[i].Position, radiusI, viewBox);
                                spaces[j].Position = ClampToViewBox(spaces[j].Position, radiusJ, viewBox);
                            }
                        }
                    }
                }
                while (hasOverlap);

                iteration++;
            }

            // 3) Now try to ensure each circle has at least two adjacencies, if possible.
            iteration = 0;
            while (!AllCirclesHaveTwoAdjacenciesOrNotPossible(spaces, epsilon) && iteration < maxIterations)
            {
                for (int i = 0; i < spaces.Count; i++)
                {
                    // Only attempt if there are at least two possible neighbors.
                    if (spaces.Count - 1 < 2)
                        continue;

                    if (CountAdjacencies(spaces, i, epsilon) < 2)
                    {
                        ForceSecondAdjacencyWithClosest(spaces, i, viewBox);
                    }
                }

                // Re-run collision resolution in case forcing introduced new overlaps.
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

                                spaces[i].Position = ClampToViewBox(spaces[i].Position, radiusI, viewBox);
                                spaces[j].Position = ClampToViewBox(spaces[j].Position, radiusJ, viewBox);
                            }
                        }
                    }
                }
                while (hasOverlap);

                iteration++;
            }
        }

        /// <summary>
        /// Returns true if every circle in 'spaces' touches at least one other circle.
        /// </summary>
        private bool AllCirclesHaveAdjacency(List<SpaceNode> spaces, double epsilon)
        {
            for (int i = 0; i < spaces.Count; i++)
            {
                if (!CircleHasAdjacency(spaces, i, epsilon))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Returns true if circle at 'index' touches at least one other circle.
        /// </summary>
        private bool CircleHasAdjacency(List<SpaceNode> spaces, int index, double epsilon)
        {
            double rI = GetCircleRadius(spaces[index].Area);
            XYZ posI = spaces[index].Position;

            for (int j = 0; j < spaces.Count; j++)
            {
                if (j == index) continue;
                double rJ = GetCircleRadius(spaces[j].Area);
                double dist = (posI - spaces[j].Position).GetLength();
                if (Math.Abs(dist - (rI + rJ)) < epsilon)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns the number of circles that are adjacent to the circle at 'index'.
        /// </summary>
        private int CountAdjacencies(List<SpaceNode> spaces, int index, double epsilon)
        {
            int count = 0;
            double rI = GetCircleRadius(spaces[index].Area);
            XYZ posI = spaces[index].Position;

            for (int j = 0; j < spaces.Count; j++)
            {
                if (j == index) continue;
                double rJ = GetCircleRadius(spaces[j].Area);
                double dist = (posI - spaces[j].Position).GetLength();
                if (Math.Abs(dist - (rI + rJ)) < epsilon)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Returns true if for every circle, if there are at least two other circles available,
        /// then it touches at least two; otherwise returns true (i.e. not possible to have two).
        /// </summary>
        private bool AllCirclesHaveTwoAdjacenciesOrNotPossible(List<SpaceNode> spaces, double epsilon)
        {
            for (int i = 0; i < spaces.Count; i++)
            {
                if (spaces.Count - 1 >= 2 && CountAdjacencies(spaces, i, epsilon) < 2)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Forces the circle at 'index' to be adjacent to its closest neighbor.
        /// </summary>
        private void ForceAdjacencyWithClosest(List<SpaceNode> spaces, int index, BoundingBoxXYZ viewBox)
        {
            SpaceNode circleI = spaces[index];
            double rI = GetCircleRadius(circleI.Area);
            int closestIndex = -1;
            double closestDist = double.MaxValue;

            for (int j = 0; j < spaces.Count; j++)
            {
                if (j == index) continue;
                double dist = (circleI.Position - spaces[j].Position).GetLength();
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestIndex = j;
                }
            }

            if (closestIndex >= 0)
            {
                SpaceNode circleJ = spaces[closestIndex];
                double rJ = GetCircleRadius(circleJ.Area);
                XYZ delta = circleI.Position - circleJ.Position;
                double distance = delta.GetLength();
                if (distance < 1e-6)
                    delta = new XYZ(1, 0, 0);
                XYZ direction = delta.Normalize();
                double targetDistance = rI + rJ;
                circleI.Position = circleJ.Position + direction * targetDistance;
                circleI.Position = ClampToViewBox(circleI.Position, rI, viewBox);
            }
        }

        /// <summary>
        /// Forces a second adjacency for the circle at 'index' by repositioning it so that
        /// it becomes tangent to both its already–adjacent neighbor and its next–closest candidate.
        /// Only performs the move if the new position does not overlap other circles and remains in view.
        /// Returns true if successful.
        /// </summary>
        private bool ForceSecondAdjacencyWithClosest(List<SpaceNode> spaces, int index, BoundingBoxXYZ viewBox)
        {
            SpaceNode circleI = spaces[index];
            double rI = GetCircleRadius(circleI.Area);

            // Identify the already adjacent circle (first neighbor).
            int firstAdj = -1;
            for (int j = 0; j < spaces.Count; j++)
            {
                if (j == index) continue;
                double rJ = GetCircleRadius(spaces[j].Area);
                double dist = (circleI.Position - spaces[j].Position).GetLength();
                if (Math.Abs(dist - (rI + rJ)) < 0.001)
                {
                    firstAdj = j;
                    break;
                }
            }
            if (firstAdj < 0)
                return false; // Cannot force a second if no first exists.

            // Find the next–closest candidate that is not already adjacent.
            int candidateIndex = -1;
            double closestDist = double.MaxValue;
            for (int j = 0; j < spaces.Count; j++)
            {
                if (j == index || j == firstAdj)
                    continue;
                double rJ = GetCircleRadius(spaces[j].Area);
                double dist = (circleI.Position - spaces[j].Position).GetLength();
                if (Math.Abs(dist - (rI + rJ)) >= 0.001 && dist < closestDist)
                {
                    closestDist = dist;
                    candidateIndex = j;
                }
            }
            if (candidateIndex < 0)
                return false; // No candidate available.

            SpaceNode circleCandidate = spaces[candidateIndex];
            double rCandidate = GetCircleRadius(circleCandidate.Area);
            SpaceNode circleFirst = spaces[firstAdj];
            double rFirst = GetCircleRadius(circleFirst.Area);

            // We now look for a new position for circleI that is tangent to both circleFirst and circleCandidate.
            XYZ posA = circleFirst.Position;
            XYZ posB = circleCandidate.Position;
            double R1 = rI + rFirst;
            double R2 = rI + rCandidate;
            XYZ diff = posB - posA;
            double d = diff.GetLength();

            if (d > R1 + R2 || d < Math.Abs(R1 - R2))
                return false; // No intersection exists.

            double a = (R1 * R1 - R2 * R2 + d * d) / (2 * d);
            double h = Math.Sqrt(Math.Max(0, R1 * R1 - a * a));
            XYZ ex = diff.Normalize();
            // In 2D, a perpendicular vector can be defined as:
            XYZ perp = new XYZ(-ex.Y, ex.X, 0);

            XYZ intersection1 = posA + ex * a + perp * h;
            XYZ intersection2 = posA + ex * a - perp * h;

            // Pick an intersection that is inside the view box and does not cause overlap.
            const double epsilon = 0.001;
            XYZ chosen = null;
            if (IsWithinViewBox(intersection1, rI, viewBox) &&
                !CausesOverlap(intersection1, spaces, index, epsilon, new int[] { firstAdj, candidateIndex }))
            {
                chosen = intersection1;
            }
            else if (IsWithinViewBox(intersection2, rI, viewBox) &&
                     !CausesOverlap(intersection2, spaces, index, epsilon, new int[] { firstAdj, candidateIndex }))
            {
                chosen = intersection2;
            }

            if (chosen == null)
                return false;

            circleI.Position = chosen;
            return true;
        }

        /// <summary>
        /// Returns true if the given position with 'radius' lies fully inside the view box.
        /// </summary>
        private bool IsWithinViewBox(XYZ position, double radius, BoundingBoxXYZ viewBox)
        {
            return position.X >= viewBox.Min.X + radius && position.X <= viewBox.Max.X - radius &&
                   position.Y >= viewBox.Min.Y + radius && position.Y <= viewBox.Max.Y - radius;
        }

        /// <summary>
        /// Checks whether placing circle at newPos (with index 'index') would overlap any other circle,
        /// excluding those at indices specified in ignoreIndices.
        /// </summary>
        private bool CausesOverlap(XYZ newPos, List<SpaceNode> spaces, int index, double epsilon, int[] ignoreIndices)
        {
            double rI = GetCircleRadius(spaces[index].Area);
            for (int k = 0; k < spaces.Count; k++)
            {
                if (k == index || ignoreIndices.Contains(k))
                    continue;
                double rK = GetCircleRadius(spaces[k].Area);
                double dist = (newPos - spaces[k].Position).GetLength();
                if (dist < (rI + rK - epsilon))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Clamps the given center point so the entire circle stays inside the bounding box.
        /// </summary>
        private XYZ ClampToViewBox(XYZ position, double radius, BoundingBoxXYZ viewBox)
        {
            double clampedX = Math.Max(viewBox.Min.X + radius, Math.Min(position.X, viewBox.Max.X - radius));
            double clampedY = Math.Max(viewBox.Min.Y + radius, Math.Min(position.Y, viewBox.Max.Y - radius));
            return new XYZ(clampedX, clampedY, position.Z);
        }







        public bool VerifyBoundaryLinesAndDisplay(List<Line> boundaryLines)
        {
            string message = "";

            // Check for null or empty list.
            if (boundaryLines == null || boundaryLines.Count == 0)
            {
                TaskDialog.Show("Boundary Lines Verification", "Boundary lines list is null or empty.");
                return false;
            }
            message += $"Boundary lines count: {boundaryLines.Count}\n";

            // A valid polygon should have at least 3 segments.
            if (boundaryLines.Count < 3)
            {
                message += "Not enough boundary lines to form a polygon (minimum 3 required).";
                TaskDialog.Show("Boundary Lines Verification", message);
                return false;
            }

            // Verify continuity: Check each line's end matches the next line's start.
            for (int i = 0; i < boundaryLines.Count - 1; i++)
            {
                XYZ endPoint = boundaryLines[i].GetEndPoint(1);
                XYZ nextStart = boundaryLines[i + 1].GetEndPoint(0);
                if (!endPoint.IsAlmostEqualTo(nextStart))
                {
                    message += $"Discontinuity between line {i} and line {i + 1}: {endPoint} does not match {nextStart}.\n";
                    TaskDialog.Show("Boundary Lines Verification", message);
                    return false;
                }
            }

            // Verify closure: The end of the last line should match the start of the first line.
            if (!boundaryLines[boundaryLines.Count - 1].GetEndPoint(1).IsAlmostEqualTo(boundaryLines[0].GetEndPoint(0)))
            {
                message += "The last boundary line does not close the loop with the first boundary line.";
                TaskDialog.Show("Boundary Lines Verification", message);
                return false;
            }

            message += "Boundary lines verified: continuous and forming a closed loop.";
            TaskDialog.Show("Boundary Lines Verification", message);
            return true;
        }


        public List<Line> FixDiscontinuityLines(List<Line> boundaryLines, double snapTolerance = 1e-6)
        {
            if (boundaryLines == null || boundaryLines.Count == 0)
            {
                TaskDialog.Show("Fix Discontinuities", "Boundary lines list is null or empty.");
                return boundaryLines;
            }

            List<Line> fixedLines = new List<Line>();
            fixedLines.Add(boundaryLines[0]);

            for (int i = 1; i < boundaryLines.Count; i++)
            {
                // Use the last line in fixedLines.
                XYZ previousEnd = fixedLines[fixedLines.Count - 1].GetEndPoint(1);
                XYZ currentStart = boundaryLines[i].GetEndPoint(0);
                XYZ currentEnd = boundaryLines[i].GetEndPoint(1);

                // Snap the start point if within tolerance.
                if (!previousEnd.IsAlmostEqualTo(currentStart))
                {
                    double distance = previousEnd.DistanceTo(currentStart);
                    if (distance <= snapTolerance)
                    {
                        currentStart = previousEnd;
                    }
                    else
                    {
                        // Optionally force snap even if gap is larger.
                        currentStart = previousEnd;
                    }
                }

                double newLength = currentEnd.DistanceTo(currentStart);
                // Only add the line if it's longer than our tolerance.
                if (newLength > snapTolerance)
                {
                    fixedLines.Add(Line.CreateBound(currentStart, currentEnd));
                }
                else
                {
                    TaskDialog.Show("Fix Discontinuities", $"Skipped a line at index {i} due to insufficient length.");
                }
            }

            // Ensure the loop is closed.
            if (fixedLines.Count > 0)
            {
                XYZ firstPoint = fixedLines[0].GetEndPoint(0);
                XYZ lastEnd = fixedLines[fixedLines.Count - 1].GetEndPoint(1);
                if (!lastEnd.IsAlmostEqualTo(firstPoint))
                {
                    double finalLength = lastEnd.DistanceTo(firstPoint);
                    if (finalLength > snapTolerance)
                    {
                        fixedLines[fixedLines.Count - 1] = Line.CreateBound(fixedLines[fixedLines.Count - 1].GetEndPoint(0), firstPoint);
                        TaskDialog.Show("Fix Discontinuities", "Adjusted final line to close the loop.");
                    }
                    else
                    {
                        TaskDialog.Show("Fix Discontinuities", "Final segment length is too short to adjust.");
                    }
                }
            }

            return fixedLines;
        }





        // Helper method to reorder and orient the lines into a continuous loop.
        List<Line> ReorderLines(List<Line> lines)
        {
            // Create a working copy.
            List<Line> remaining = new List<Line>(lines);
            List<Line> ordered = new List<Line>();

            if (remaining.Count == 0)
                return ordered;

            // Start with the first line.
            ordered.Add(remaining[0]);
            remaining.RemoveAt(0);

            // Loop until all lines are connected.
            while (remaining.Count > 0)
            {
                // Get the end point of the last added line.
                XYZ lastPoint = ordered.Last().GetEndPoint(1);
                bool foundMatch = false;

                // Try to find a line whose start or end matches the last point.
                for (int i = 0; i < remaining.Count; i++)
                {
                    Line candidate = remaining[i];
                    if (lastPoint.IsAlmostEqualTo(candidate.GetEndPoint(0)))
                    {
                        ordered.Add(candidate);
                        remaining.RemoveAt(i);
                        foundMatch = true;
                        break;
                    }
                    else if (lastPoint.IsAlmostEqualTo(candidate.GetEndPoint(1)))
                    {
                        // Reverse the candidate so that its start matches the last point.
                        Line reversed = Line.CreateBound(candidate.GetEndPoint(1), candidate.GetEndPoint(0));
                        ordered.Add(reversed);
                        remaining.RemoveAt(i);
                        foundMatch = true;
                        break;
                    }
                }

                if (!foundMatch)
                {
                    throw new Exception("Cannot form a continuous loop with the provided lines.");
                }
            }
            return ordered;
        }


       

    }
   

}