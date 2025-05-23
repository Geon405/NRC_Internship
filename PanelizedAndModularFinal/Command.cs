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
using System.Linq;               // for OrderBy/ThenBy/First
using System.Diagnostics;        // for Process.GetCurrentProcess()
using System.Windows.Interop;    // for WindowInteropHelper
using PanelizedAndModularFinal;  // for SpaceNode, GraphEvaluator, etc.
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
        private static readonly Random _rng = new Random(1234567);


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
                            //Random random = new Random();

                            foreach (var inst in secondWindow.Instances)
                            {
                                double area = inst.Area < 10.0 ? 25.0 : inst.Area;
                                View activeView1 = doc.ActiveView;
                                BoundingBoxXYZ viewBox1 = activeView1.CropBoxActive && activeView1.CropBox != null
                                    ? activeView1.CropBox
                                    : activeView1.get_BoundingBox(null);
                                double layoutWidth1 = viewBox1.Max.X - viewBox1.Min.X;
                                double layoutHeight1 = viewBox1.Max.Y - viewBox1.Min.Y;
                                XYZ position = new XYZ(
                                    viewBox1.Min.X + _rng.NextDouble() * layoutWidth1,
                                    viewBox1.Min.Y + _rng.NextDouble() * layoutHeight1,
                                    0); var node = new SpaceNode(inst.Name, inst.RoomType, area, position, inst.WpfColor);
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

                                int[,] adjacencyMatrix = adjacencyWindow.PreferredAdjacency;


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
                                    // right after you pull the raw matrix out of the dialog:
                                    int[,] connectivityMatrix = connectivityWindow.ConnectivityMatrix;
                                    int m = connectivityMatrix.GetLength(0);

                                    // 1) clone the _raw_ clicks into rawConnectivity
                                    var rawConnectivity = new int[m, m];
                                    for (int i = 0; i < m; i++)
                                        for (int j = 0; j < m; j++)
                                            rawConnectivity[i, j] = connectivityMatrix[i, j];

                                    // 2) now symmetrize + transitive‐close in-place on connectivityMatrix
                                    for (int i = 0; i < m; i++)
                                        for (int j = i + 1; j < m; j++)
                                            if (connectivityMatrix[i, j] == 1 || connectivityMatrix[j, i] == 1)
                                                connectivityMatrix[i, j] = connectivityMatrix[j, i] = 1;

                                    // 2a) also force every “must‐touch” adjacency into the connectivity graph:
                                    for (int i = 0; i < m; i++)
                                        for (int j = i + 1; j < m; j++)
                                            if (adjacencyMatrix[i, j] == 1)
                                                connectivityMatrix[i, j] =
                                                connectivityMatrix[j, i] = 1;

                                    for (int k = 0; k < m; k++)
                                        for (int i = 0; i < m; i++)
                                            for (int j = 0; j < m; j++)
                                                if (connectivityMatrix[i, k] == 1 && connectivityMatrix[k, j] == 1)
                                                    connectivityMatrix[i, j] = connectivityMatrix[j, i] = 1;

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

                                        // ── 1) Gather required vs optional edges ──
                                        //    (we keep adjacencyMatrix[i,j]==1 as your hard‐adjacency,
                                        //     and connectivityMatrix only drives your connectivity CHECK LATER)

                                        int n = adjacencyMatrix.GetLength(0);

                                        // 1) Copy the *raw* user clicks for optional‐edge enumeration
                                        var rawConn = new int[n, n];
                                           for (int i = 0; i < n; i++)
                                                 for (int j = 0; j < n; j++)
                                            rawConn[i, j] = connectivityMatrix[i, j];

                                        // 2) Symmetrize + transitive‐close on connectivityMatrix in place
                                        for (int i = 0; i < n; i++)
                                            for (int j = i + 1; j < n; j++)
                                                if (connectivityMatrix[i, j] == 1 || connectivityMatrix[j, i] == 1)
                                                    connectivityMatrix[i, j] = connectivityMatrix[j, i] = 1;
                                        for (int k = 0; k < n; k++)
                                            for (int i = 0; i < n; i++)
                                                for (int j = 0; j < n; j++)
                                                    if (connectivityMatrix[i, k] == 1 && connectivityMatrix[k, j] == 1)
                                                        connectivityMatrix[i, j] = connectivityMatrix[j, i] = 1;

                                        // 3) Build required vs optional edge lists
                                        var required = new List<(int i, int j)>();
                                        var optional = new List<(int i, int j)>();
                                           for (int i = 0; i < n; i++)
                                                 for (int j = i + 1; j < n; j++)
                                                 {
                                                   if (adjacencyMatrix[i, j] == 1)
                                                required.Add((i, j));
                                                   else if (rawConn[i, j] == 1)      // <-- only those user actually allowed
                                                optional.Add((i, j));
                                                 }

                                        // ── 4) Enumerate *all* valid adjacency‐sets (no fixed limit)
                                        //    required = your “must‐touch” edges
                                        //    optional = connectivity‐only edges the user clicked
                                        int M = optional.Count;
                                        var validAdjSets = new List<int[,]>();

                                        for (int mask = 0; mask < (1 << M); mask++)
                                        {
                                            // 4a) build candidate adjacency
                                            var candAdj = new int[n, n];

                                            // 4b) required edges (hard‐adjacency)
                                            foreach (var (i, j) in required)
                                                candAdj[i, j] = candAdj[j, i] = 1;

                                            // 4c) optional edges per this subset
                                            for (int b = 0; b < M; b++)
                                            {
                                                if ((mask & (1 << b)) != 0)
                                                {
                                                    var (i, j) = optional[b];
                                                    candAdj[i, j] = candAdj[j, i] = 1;
                                                }
                                            }

                                            // 4d) filter by connectivity (O(n³))
                                            if (IsCandidateValid(candAdj, connectivityMatrix))
                                                validAdjSets.Add(candAdj);
                                        }

                                        var allCandidates = new List<(List<SpaceNode> layout, int[,] candidateAdj)>();

                                        foreach (var candAdj in validAdjSets)
                                        {
                                            // 5) clone your original nodes
                                            var clone = CloneSpaces(spaces);

                                            // 6) initial force-directed layout
                                            ApplyForceDirectedLayout(clone, candAdj, weightedAdjMatrix, viewBox);

                                            // 7) now *enforce* exact tangency on all hard edges
                                            //    and knock out any overlaps + re-center a few times
                                            for (int pass = 0; pass < 8; pass++)
                                            {
                                                EnforceAllDistanceConstraints(clone, candAdj, viewBox);
                                                SnapPreferredAdjacencyCircles(clone, candAdj, viewBox);
                                                ResolveCollisions(clone);
                                                ResolveBoundaryViolations(clone, viewBox);
                                                CenterLayout(clone, viewBox);
                                            }

                                            // 8) just in case a final cleanup is needed
                                            ResolveNonAdjacentCollisions(clone, candAdj);
                                            ResolveBoundaryViolations(clone, viewBox);
                                            CenterLayout(clone, viewBox);

                                            allCandidates.Add((clone, candAdj));
                                        }

                                        TaskDialog.Show("Layouts Generated",
                                        $"Based on your adjacency + connectivity, {allCandidates.Count} valid graphs were found.");

                                        // gather the layouts and connectivity matrices
                                        var layouts = allCandidates.Select(t => t.layout).ToList();
                                        var connectivities = allCandidates.Select(t => t.candidateAdj).ToList();

                                        // show our new WPF window that draws each graph
                                        var previewWin = new CandidateGraphsWindow(layouts, connectivities);
                                        new WindowInteropHelper(previewWin)
                                        {
                                            Owner = Process.GetCurrentProcess().MainWindowHandle
                                        };
                                        if (previewWin.ShowDialog() != true)
                                            return Result.Cancelled;

                                        // ── 5) Evaluate each by ASPL & density ──
                                        var evals = allCandidates
                                          .Select(t => {
                                              double aspl = GraphEvaluator.CalculateASPL(t.layout, t.candidateAdj);
                                              double gd = GraphEvaluator.CalculateDensity(t.layout, t.candidateAdj);
                                              return (t.layout, t.candidateAdj, aspl, gd);
                                          })
                                          .ToList();

                                        // find min/max for normalization
                                        double asplMin = evals.Min(e => e.aspl),
                                               asplMax = evals.Max(e => e.aspl),
                                               gdMin = evals.Min(e => e.gd),
                                               gdMax = evals.Max(e => e.gd);

                                        // weights for P(G)
                                        const double w1 = 0.7, w2 = 0.3;

                                        // ── 6) Compute P(G) and pick the best ──
                                        var bestEntry = evals
                                          .Select(e => {
                                              double normASPL = (e.aspl - asplMin) / (asplMax - asplMin);
                                              double normGD = (e.gd - gdMin) / (gdMax - gdMin);
                                              double score = w1 * normASPL + w2 * normGD;
                                              return (e.layout, e.candidateAdj, score);
                                          })
                                          .OrderBy(x => x.score)
                                          .First();

                                        var best = bestEntry.layout;
                                        var bestAdj = bestEntry.candidateAdj;

                                        // ── 7) Enforce exact tangency & clean-up on the best ──
                                        for (int pass = 0; pass < 8; pass++)
                                        {
                                            EnforceAllDistanceConstraints(best, bestAdj, viewBox);
                                            SnapPreferredAdjacencyCircles(best, bestAdj, viewBox);
                                            ResolveCollisions(best);
                                            ResolveBoundaryViolations(best, viewBox);
                                            CenterLayout(best, viewBox);
                                        }
                                        for (int t = 0; t < 4; t++)
                                        {
                                            ResolveNonAdjacentCollisions(best, bestAdj);
                                            ResolveBoundaryViolations(best, viewBox);
                                            CenterLayout(best, viewBox);
                                        }
                                        CenterLayout(best, viewBox);
                                        const int MAX_STABLE = 50;
                                        for (int pass = 0; pass < MAX_STABLE; pass++)
                                        {
                                            EnforceAllDistanceConstraints(best, bestAdj, viewBox);
                                            SnapPreferredAdjacencyCircles(best, bestAdj, viewBox);
                                            ResolveCollisions(best);
                                            ResolveBoundaryViolations(best, viewBox);
                                            if (!SnapPreferredAdjacencyCirclesOnce(best, bestAdj, viewBox))
                                                break;
                                        }
                                        EnsureAllCirclesConnected(best, viewBox);
                                        ResolveNonAdjacentCollisions(best, bestAdj);
                                        ResolveBoundaryViolations(best, viewBox);

                                        // 3) Preview it
                                        var preview = new ChosenLayoutPreviewWindow(best);
                                        new WindowInteropHelper(preview) { Owner = Process.GetCurrentProcess().MainWindowHandle };
                                        if (preview.ShowDialog() != true)
                                            return Result.Cancelled;

                                        // … then draw in Revit …

                                        // 4) Draw the chosen layout
                                        GlobalData.SavedSpaces = best;
                                        using (var tx = new Transaction(doc, "Draw Auto-Selected Layout"))
                                        {
                                            tx.Start();

                                            // draw each connection line
                                            for (int i = 0; i < best.Count; i++)
                                            {
                                                for (int j = i + 1; j < best.Count; j++)
                                                {
                                                    if (weightedAdjMatrix[i, j].HasValue)
                                                    {
                                                        var revitLine = Line.CreateBound(
                                                            best[i].Position,
                                                            best[j].Position);
                                                        var detail = doc.Create.NewDetailCurve(doc.ActiveView, revitLine);

                                                        GlobalData.SavedConnectionLines.Add(detail.Id);
                                                        GlobalData.Step1Elements.Add(detail.Id);
                                                    }
                                                }
                                            }

                                            // draw each room‐circle
                                            foreach (var space in best)
                                            {
                                                CreateCircleNode(
                                                    doc,
                                                    space.Position,
                                                    space.Area,
                                                    space.WpfColor,
                                                    space.Name,
                                                    uidoc.ActiveView.Id);
                                            }

                                            tx.Commit();
                                        }

                                        // 5) Let the user confirm before proceeding
                                        TaskDialog.Show(
                                            "Step 1 Complete",
                                            $"Automatically created {best.Count} room(s) with connections.\n\nClick OK to proceed to Step 2."
                                        );

                                        // 6) Clear Step 1 graphics
                                        using (var tx2 = new Transaction(doc, "Clear Step 1 Output"))
                                        {
                                            tx2.Start();
                                            foreach (var id in GlobalData.Step1Elements)
                                                doc.Delete(id);
                                            tx2.Commit();
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
                                            List<ModuleArrangementResult> allArrangements = arrangement.GetValidArrangements();

                                            if (allArrangements.Count == 0)
                                            {
                                                string message2 =
                                                    $"No valid arrangements found. Module width or height may exceed land dimensions.\n" +
                                                    $"Land Width: {GlobalData.landWidth:F2}, Land Height: {GlobalData.landHeight:F2}\n" +
                                                    $"Module Width: {minWidth:F2}, Max Height: {maxHeight:F2}\n\n" +
                                                    "Please try another combination.";

                                                TaskDialog.Show("Arrangement Error", message2);
                                                continue; // LOOP TO LET USER PICK AGAIN
                                            }


                                            var bestByAL = allArrangements
                                             .Select(a => new { a, AL = ArrangementEvaluator.CalculateAttachmentLength(a) })
                                             .GroupBy(x => x.AL)
                                             .OrderByDescending(g => g.Key)
                                             .First()
                                             .Select(x => x.a);

                                            // 3) among those, filter to minimal perimeter
                                            var optimal = bestByAL
                                                .Select(a => new { a, P = ArrangementEvaluator.CalculatePerimeter(a) })
                                                .GroupBy(x => x.P)
                                                .OrderBy(g => g.Key)
                                                .First()
                                                .Select(x => x.a)
                                                .ToList();

                                            List<List<XYZ>> perimeterCorners;
                                            var uniqueArrangements = ModuleArrangement
                                                .FilterUniqueByPerimeter(optimal, out perimeterCorners);

                                            //  arrangement.DisplayScenarioSummary(uniqueArrangements);
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

                                            arrangement.CenterArrangement(chosen);

                                            int moduleCount = chosen.PlacedModules.Count;
                                            List<ElementId> drawnIds = arrangement.DrawArrangement(doc, chosen);

                                            arrangementCreated = true;
                                        }
                                        // --- Step 2: Re-Output Saved Layout (Connection Lines and Room Circles) ---
                                        List<SpaceNode> savedSpaces = GlobalData.SavedSpaces;
                                        using (Transaction tx = new Transaction(doc, "Output Saved Layout"))
                                        {
                                            tx.Start();

                                            XYZ overallBoundaryCenter = arrangement.OverallCenter(chosen);

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
        private void ApplyForceDirectedLayout(
        List<SpaceNode> spaces,
        int[,] adjacency,       // hard-adjacency “must-touch” matrix
        double?[,] weights,     // user-specified edge weights (or null)
        BoundingBoxXYZ viewBox)
        {
            int n = spaces.Count;
            var forces = new XYZ[n];

            for (int iter = 0; iter < ITERATIONS; iter++)
            {
                // 1) Zero out all force accumulators.
                for (int i = 0; i < n; i++)
                    forces[i] = XYZ.Zero;

                // 2) Pairwise repulsion + attraction.
                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        XYZ pi = spaces[i].Position;
                        XYZ pj = spaces[j].Position;
                        XYZ delta = pj - pi;
                        double dist = Math.Max(delta.GetLength(), 1e-3);
                        XYZ dir = delta.Normalize();

                        // 2a) Universal repulsion ∝ 1 / dist²
                        double repForce = REPULSION_CONSTANT / (dist * dist);
                        XYZ repulsion = repForce * dir;

                        // 2b) Spring attraction:
                        //    • If adjacency[i,j]==1 → must‐touch spring of rest-length = rᵢ + rⱼ
                        //    • Otherwise, if weights[i,j]>0 → soft spring with rest-length slightly > rᵢ+rⱼ
                        double w = weights[i, j].GetValueOrDefault(0.0);
                        if (adjacency[i, j] == 1)
                            w = PREFERRED_ADJ_FACTOR;  // e.g. 50× stronger

                        double restLength = (spaces[i].Radius + spaces[j].Radius)
                                          + (adjacency[i, j] == 1 ? 0.0 : 5.0);

                        double attrForce = (w > 0.0)
                            ? SPRING_CONSTANT * w * (dist - restLength)
                            : 0.0;
                        XYZ attraction = -attrForce * dir;

                        // 2c) Accumulate equal-and-opposite forces
                        forces[i] += repulsion + attraction;
                        forces[j] -= repulsion + attraction;
                    }
                }

                // 3) Damping & move each node.
                double damp = ENABLE_ADAPTIVE_DAMPING
                    ? DAMPING - (DAMPING / 2.0) * (iter / (double)ITERATIONS)
                    : DAMPING;

                for (int i = 0; i < n; i++)
                {
                    XYZ velocity = forces[i] * damp;
                    const double maxDisp = 5.0;
                    if (velocity.GetLength() > maxDisp)
                        velocity = velocity.Normalize() * maxDisp;
                    spaces[i].Position += velocity;
                }

                // 4) Knock out any overlaps and clamp into the viewBox.
                ResolveCollisions(spaces);
                ResolveBoundaryViolations(spaces, viewBox);

                // 5) Early exit if things have mostly settled.
                double avgMove = forces.Sum(f => f.GetLength()) / n;
                if (avgMove < MOVEMENT_THRESHOLD)
                    break;
            }
        }

        /// <summary>
        /// Only resolves overlaps for circle‐pairs that are *not* in the hardAdj must-touch graph.
        /// </summary>
        private void ResolveNonAdjacentCollisions(
            List<SpaceNode> spaces,
            int[,] hardAdj)
        {
            const double ε = 0.01;
            bool didFix;
            do
            {
                didFix = false;
                int n = spaces.Count;
                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        // skip your must-touch pairs
                        if (hardAdj[i, j] == 1) continue;

                        XYZ pi = spaces[i].Position;
                        XYZ pj = spaces[j].Position;
                        double rI = GetCircleRadius(spaces[i].Area);
                        double rJ = GetCircleRadius(spaces[j].Area);
                        var delta = pj - pi;
                        double d = delta.GetLength();
                        double minD = rI + rJ;

                        if (d < minD)
                        {
                            didFix = true;
                            double overlap = (minD - d) + ε;
                            XYZ dir = delta.Normalize();
                            spaces[i].Position -= 0.5 * overlap * dir;
                            spaces[j].Position += 0.5 * overlap * dir;
                        }
                    }
                }
            }
            while (didFix);
        }

        private void ResolveCollisions(List<SpaceNode> spaces)
        {
            const double epsilon = 0.01;
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
                        if (direction.GetLength() < 1e-3)
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

        private void SnapConnectedCircles(List<SpaceNode> spaces, int[,] connectivityMatrix, BoundingBoxXYZ viewBox)
        {
            const double tol = 1e-3;
            bool adjusted = true;
            int iter = 0, maxIter = 20;   // increase iterations
            while (adjusted && iter++ < maxIter)
            {
                adjusted = false;
                for (int i = 0; i < spaces.Count; i++)
                {
                    for (int j = i + 1; j < spaces.Count; j++)
                    {
                        if (connectivityMatrix[i, j] == 1)
                        {
                            double rI = GetCircleRadius(spaces[i].Area);
                            double rJ = GetCircleRadius(spaces[j].Area);
                            double desired = rI + rJ;
                            var delta = spaces[j].Position - spaces[i].Position;
                            double dist = delta.GetLength();
                            if (dist > desired + tol)
                            {
                                double move = (dist - desired) / 2.0;
                                var dir = delta.Normalize();
                                spaces[i].Position += dir * move;
                                spaces[j].Position -= dir * move;
                                // **clamp** both back into the view
                                spaces[i].Position = ClampToViewBox(spaces[i].Position, rI, viewBox);
                                spaces[j].Position = ClampToViewBox(spaces[j].Position, rJ, viewBox);
                                adjusted = true;
                            }
                        }
                    }
                }
            }
        }

        private void SnapPreferredAdjacencyCircles(
            List<SpaceNode> spaces, int[,] pref, BoundingBoxXYZ viewBox)
        {
            const double tol = 1e-6;
            int n = spaces.Count;

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (pref[i, j] != 1) continue;

                    var A = spaces[i];
                    var B = spaces[j];
                    double rA = GetCircleRadius(A.Area),
                           rB = GetCircleRadius(B.Area);
                    XYZ delta = B.Position - A.Position;
                    double d = delta.GetLength();
                    double target = rA + rB;

                    if (Math.Abs(d - target) > tol)
                    {
                        var dir = (d < tol) ? new XYZ(1, 0, 0) : delta.Normalize();
                        double overshoot = d - target;
                        // push *both* halves
                        A.Position += dir * (overshoot * 0.5);
                        B.Position -= dir * (overshoot * 0.5);
                        // clamp back into view
                        A.Position = ClampToViewBox(A.Position, rA, viewBox);
                        B.Position = ClampToViewBox(B.Position, rB, viewBox);
                    }
                }
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

        /// enforce hard‐adjacency to exact tangency:
        private void EnforceAllDistanceConstraints(
            List<SpaceNode> spaces,
            int[,] adjacency,
            BoundingBoxXYZ viewBox)
        {
            int n = spaces.Count;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (adjacency[i, j] == 1)
                    {
                        double rI = GetCircleRadius(spaces[i].Area);
                        double rJ = GetCircleRadius(spaces[j].Area);
                        var delta = spaces[j].Position - spaces[i].Position;
                        double d = Math.Max(delta.GetLength(), 1e-3);
                        var dir = delta.Normalize();
                        double diff = d - (rI + rJ);
                        spaces[i].Position += dir * (diff * 0.5);
                        spaces[j].Position -= dir * (diff * 0.5);
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

        /// <summary>
        /// Attempts one “snap‐to‐tangency” pass for every pref[i,j]==1 edge.
        /// Returns true if any circle was moved.
        /// </summary>
        private bool SnapPreferredAdjacencyCirclesOnce(
            List<SpaceNode> spaces,
            int[,] pref,
            BoundingBoxXYZ viewBox)
        {
            const double tol = 1e-6;
            bool moved = false;
            int n = spaces.Count;

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (pref[i, j] != 1) continue;

                    var A = spaces[i];
                    var B = spaces[j];
                    double rA = GetCircleRadius(A.Area);
                    double rB = GetCircleRadius(B.Area);

                    XYZ delta = B.Position - A.Position;
                    double d = delta.GetLength();
                    double target = rA + rB;

                    if (Math.Abs(d - target) > tol)
                    {
                        XYZ dir = (d < tol) ? new XYZ(1, 0, 0) : delta.Normalize();
                        double overshoot = d - target;

                        // push/pull each half‐way
                        A.Position += dir * (overshoot * 0.5);
                        B.Position -= dir * (overshoot * 0.5);

                        // clamp back into the view
                        A.Position = ClampToViewBox(A.Position, rA, viewBox);
                        B.Position = ClampToViewBox(B.Position, rB, viewBox);

                        moved = true;
                    }
                }
            }

            return moved;
        }

        private bool IsCandidateValid(int[,] hardAdj, int[,] conn)
        {
            int n = conn.GetLength(0);
            // build an adjacency‐list or just do an O(n²) DFS for each pair where conn[i,j]==1
            bool[,] seen = new bool[n, n];
            for (int i = 0; i < n; i++)
            {
                // BFS/DFS from i on hardAdj
                var stack = new Stack<int>();
                var vis = new bool[n];
                stack.Push(i); vis[i] = true;
                while (stack.Count > 0)
                {
                    int u = stack.Pop();
                    for (int v = 0; v < n; v++)
                    {
                        if (hardAdj[u, v] == 1 && !vis[v])
                        {
                            vis[v] = true; stack.Push(v);
                        }
                    }
                }
                for (int j = 0; j < n; j++)
                {
                    if (conn[i, j] == 1 && !vis[j])
                        return false;
                }
            }
            return true;
        }




        /// <summary>
        /// Slides clusters together until the tangency graph is one connected component.
        /// </summary>
        private void EnsureAllCirclesConnected(List<SpaceNode> spaces, BoundingBoxXYZ viewBox)
        {
            const double eps = 0.001;
            int n = spaces.Count;

            // Find connected components under “exact” tangency
            List<List<int>> FindComponents()
            {
                var visited = new bool[n];
                var comps = new List<List<int>>();
                for (int i = 0; i < n; i++)
                {
                    if (visited[i]) continue;
                    var stack = new Stack<int>();
                    var comp = new List<int>();
                    stack.Push(i);
                    visited[i] = true;
                    while (stack.Count > 0)
                    {
                        int u = stack.Pop();
                        comp.Add(u);
                        for (int v = 0; v < n; v++)
                        {
                            if (visited[v]) continue;
                            double d = (spaces[u].Position - spaces[v].Position).GetLength();
                            double rSum = GetCircleRadius(spaces[u].Area) + GetCircleRadius(spaces[v].Area);
                            if (Math.Abs(d - rSum) < eps)
                            {
                                visited[v] = true;
                                stack.Push(v);
                            }
                        }
                    }
                    comps.Add(comp);
                }
                return comps;
            }

            // Keep gluing the two closest clusters until only one remains
            while (true)
            {
                var comps = FindComponents();
                if (comps.Count < 2) break;

                var main = comps[0];
                var isle = comps[1];

                // find the closest pair (i in main, j in isle)
                double bestDist = double.MaxValue;
                int bi = main[0], bj = isle[0];
                foreach (int i in main)
                    foreach (int j in isle)
                    {
                        double d = (spaces[i].Position - spaces[j].Position).GetLength();
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bi = i;
                            bj = j;
                        }
                    }

                // snap j so it exactly kisses i
                var A = spaces[bi];
                var B = spaces[bj];
                double rA = GetCircleRadius(A.Area), rB = GetCircleRadius(B.Area);
                var dir = (B.Position - A.Position).Normalize();
                B.Position = A.Position + dir * (rA + rB);

                // locally tidy up
                ResolveCollisions(spaces);
                ResolveBoundaryViolations(spaces, viewBox);
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


        public List<Line> FixDiscontinuityLines(List<Line> boundaryLines, double snapTolerance = 1e-3)
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
    }
}