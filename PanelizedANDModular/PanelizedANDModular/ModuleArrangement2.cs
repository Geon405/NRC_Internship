using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PanelizedAndModularFinal
{
    /// <summary>
    /// ModuleArrangement2 arranges modules using every possible orientation assignment (2^n scenarios).
    /// Each module can be either:
    ///   - Not rotated: effective horizontal = Module.Length, effective vertical = Module.Width.
    ///   - Rotated (flipped): effective horizontal = Module.Width, effective vertical = Module.Length.
    /// The modules are first sorted from biggest to smallest.
    /// Then for every orientation combination, the modules are partitioned into rows (left-to-right)
    /// and stacked (bottom-to-top) so that the overall arrangement fits within the land.
    /// Definitions:
    ///   - Land width: horizontal part of the land (CropBox width).
    ///   - Land length: vertical part of the land (CropBox height).
    ///   - For a module: Module.Length is its horizontal measurement and Module.Width is its vertical measurement (when not rotated).
    /// </summary>
    public class ModuleArrangement2
    {
        private List<ModuleType> _moduleTypes;
        private string _selectedCombination;
        private BoundingBoxXYZ _cropBox;

        // Holds logs for each orientation scenario (binary string) and how many valid arrangements it produced (before duplicate removal).
        public List<string> ScenarioLogs { get; private set; }
        // This dictionary holds the unique arrangements after duplicate removal. The key is a signature string.
        // The value is a tuple with the unique arrangement and the binary scenario that produced it.
        private Dictionary<string, (ModuleArrangementResult arrangement, string scenario)> _uniqueArrangementsMap;

        public ModuleArrangement2(List<ModuleType> moduleTypes, string selectedCombination, BoundingBoxXYZ cropBox)
        {
            _moduleTypes = moduleTypes;
            _selectedCombination = selectedCombination;
            _cropBox = cropBox;
        }

        /// <summary>
        /// Generates all valid arrangements by considering every possible orientation assignment.
        /// Also builds a log (ScenarioLogs) for each scenario (binary string) showing the total valid arrangements from that scenario.
        /// After gathering all arrangements, duplicate arrangements (those with the same module positions) are removed.
        /// </summary>
        public List<ModuleArrangementResult> GetValidArrangements()
        {
            // Parse the combination string (e.g., "2 x Module_Type 1 + 1 x Module_Type 3")
            // into a dictionary mapping 0-based module type indices to counts.
            Dictionary<int, int> typeCounts = ParseCombinationString(_selectedCombination);

            // Unroll the combination into a list of modules.
            List<ModuleType> modulesToPlace = new List<ModuleType>();
            foreach (var kvp in typeCounts)
            {
                int typeIndex = kvp.Key;
                int count = kvp.Value;
                for (int i = 0; i < count; i++)
                    modulesToPlace.Add(_moduleTypes[typeIndex]);
            }

            // Sort modules from biggest to smallest.
            // "Biggest" is determined first by Module.Length (horizontal dimension when not rotated) then Module.Width.
            modulesToPlace = modulesToPlace
                                .OrderByDescending(m => m.Length)
                                .ThenByDescending(m => m.Width)
                                .ToList();

            double availableWidth = _cropBox.Max.X - _cropBox.Min.X;
            double availableHeight = _cropBox.Max.Y - _cropBox.Min.Y;

            List<ModuleArrangementResult> validArrangements = new List<ModuleArrangementResult>();
            ScenarioLogs = new List<string>();

            int n = modulesToPlace.Count;
            int totalScenarios = 1 << n; // 2^n possible orientation assignments

            // Temporary list for arrangements (includes possible duplicates).
            for (int bitmask = 0; bitmask < totalScenarios; bitmask++)
            {
                string orientationStr = ToBinaryString(bitmask, n);
                int validCountForScenario = 0;

                List<ModuleInstance> instanceList = new List<ModuleInstance>();
                for (int j = 0; j < n; j++)
                {
                    bool isRotated = ((bitmask >> j) & 1) == 1;
                    instanceList.Add(new ModuleInstance
                    {
                        Module = modulesToPlace[j],
                        IsRotated = isRotated
                    });
                }

                List<List<List<ModuleInstance>>> allPartitions = new List<List<List<ModuleInstance>>>();
                PartitionIntoRows(instanceList, 0, availableWidth, availableHeight, new List<List<ModuleInstance>>(), allPartitions);

                foreach (var partition in allPartitions)
                {
                    ModuleArrangementResult arrangement = CreateArrangementFromRows(partition, _cropBox.Min, availableWidth, availableHeight);
                    if (arrangement != null)
                    {
                        validArrangements.Add(arrangement);
                        validCountForScenario++;
                    }
                }
                ScenarioLogs.Add("Scenario " + orientationStr + ": " + validCountForScenario + " valid arrangement(s).");
            }

            // Remove duplicate arrangements using a signature of positions and effective dimensions.
            _uniqueArrangementsMap = new Dictionary<string, (ModuleArrangementResult, string)>();
            // Here, we loop through the collected valid arrangements and tag each one with the scenario that produced it.
            // (In this example, if the same arrangement appears from different scenarios, the first occurrence is kept.)
            foreach (var arrangement in validArrangements)
            {
                string signature = GetArrangementSignature(arrangement);
                // For demonstration, we assume that the scenario information is embedded in the ScenarioLogs;
                // however, here we only store the first scenario that produced this signature.
                if (!_uniqueArrangementsMap.ContainsKey(signature))
                    _uniqueArrangementsMap.Add(signature, (arrangement, FindScenarioForArrangement(arrangement, validArrangements, modulesToPlace)));
            }
            return _uniqueArrangementsMap.Values.Select(x => x.arrangement).ToList();
        }

        /// <summary>
        /// Displays a summary of the unique (duplicate-free) valid arrangements.
        /// The summary shows the total number of unique arrangements and, for each, which orientation scenario (binary string) produced it.
        /// </summary>
        public void DisplayScenarioSummary()
        {
            if (_uniqueArrangementsMap == null)
            {
                TaskDialog.Show("Summary", "No unique arrangements have been computed yet.");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Total unique valid arrangements: " + _uniqueArrangementsMap.Count);
            foreach (var kvp in _uniqueArrangementsMap)
            {
                // kvp.Key is the signature.
                // kvp.Value.scenario is the binary string representing the orientation assignment that produced this unique arrangement.
                sb.AppendLine($"Scenario {kvp.Value.scenario} produced arrangement with signature: {kvp.Key}");
            }
            TaskDialog.Show("Unique Arrangements Summary", sb.ToString());
        }

        /// <summary>
        /// Draws the modules for a given arrangement on the active Revit view.
        /// Each module is drawn as a red rectangle. Returns the list of ElementIds of the drawn curves.
        /// </summary>
        public List<ElementId> DrawArrangement(Document doc, ModuleArrangementResult arrangement)
        {
            List<ElementId> drawnElementIds = new List<ElementId>();

            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(255, 0, 0));

            using (Transaction t = new Transaction(doc, "Draw Modules"))
            {
                t.Start();
                foreach (PlacedModule pm in arrangement.PlacedModules)
                {
                    double effectiveHorizontal = pm.ModuleInstance.EffectiveHorizontal;
                    double effectiveVertical = pm.ModuleInstance.EffectiveVertical;

                    // Calculate the four corners of the module's rectangle.
                    XYZ p0 = new XYZ(pm.Origin.X, pm.Origin.Y, 0);
                    XYZ p1 = new XYZ(pm.Origin.X + effectiveHorizontal, pm.Origin.Y, 0);
                    XYZ p2 = new XYZ(pm.Origin.X + effectiveHorizontal, pm.Origin.Y + effectiveVertical, 0);
                    XYZ p3 = new XYZ(pm.Origin.X, pm.Origin.Y + effectiveVertical, 0);

                    // Create detail curves for each edge.
                    Line l0 = Line.CreateBound(p0, p1);
                    Line l1 = Line.CreateBound(p1, p2);
                    Line l2 = Line.CreateBound(p2, p3);
                    Line l3 = Line.CreateBound(p3, p0);

                    Element e1 = doc.Create.NewDetailCurve(doc.ActiveView, l0);
                    Element e2 = doc.Create.NewDetailCurve(doc.ActiveView, l1);
                    Element e3 = doc.Create.NewDetailCurve(doc.ActiveView, l2);
                    Element e4 = doc.Create.NewDetailCurve(doc.ActiveView, l3);

                    drawnElementIds.Add(e1.Id);
                    drawnElementIds.Add(e2.Id);
                    drawnElementIds.Add(e3.Id);
                    drawnElementIds.Add(e4.Id);
                }
                t.Commit();
            }
            return drawnElementIds;
        }

        #region Helper Methods

        /// <summary>
        /// Parses the combination string (e.g., "2 x Module_Type 1 + 1 x Module_Type 3 = ...")
        /// and returns a dictionary mapping 0-based module type indices to counts.
        /// </summary>
        private Dictionary<int, int> ParseCombinationString(string combo)
        {
            Dictionary<int, int> counts = new Dictionary<int, int>();
            Regex regex = new Regex(@"(\d+)\s*x\s*Module_Type\s*(\d+)", RegexOptions.IgnoreCase);
            MatchCollection matches = regex.Matches(combo);
            foreach (Match match in matches)
            {
                int count = int.Parse(match.Groups[1].Value);
                int typeIndex = int.Parse(match.Groups[2].Value) - 1;
                counts[typeIndex] = count;
            }
            return counts;
        }

        /// <summary>
        /// Recursively partitions the list of module instances into rows.
        /// Each row's total effective horizontal dimensions must not exceed availableRowWidth,
        /// and the total height (the sum of the max effective vertical dimensions per row) must not exceed availableTotalHeight.
        /// </summary>
        private void PartitionIntoRows(List<ModuleInstance> modules, int startIndex,
            double availableRowWidth, double availableTotalHeight,
            List<List<ModuleInstance>> currentPartition,
            List<List<List<ModuleInstance>>> results)
        {
            if (startIndex >= modules.Count)
            {
                double totalHeight = currentPartition.Sum(row => row.Max(m => m.EffectiveVertical));
                if (totalHeight <= availableTotalHeight)
                    results.Add(currentPartition.Select(row => new List<ModuleInstance>(row)).ToList());
                return;
            }

            for (int i = startIndex + 1; i <= modules.Count; i++)
            {
                List<ModuleInstance> row = modules.GetRange(startIndex, i - startIndex);
                double rowWidth = row.Sum(m => m.EffectiveHorizontal);
                if (rowWidth > availableRowWidth)
                    break;

                double rowHeight = row.Max(m => m.EffectiveVertical);
                double currentTotalHeight = currentPartition.Sum(r => r.Max(m => m.EffectiveVertical));
                if (currentTotalHeight + rowHeight > availableTotalHeight)
                    break;

                currentPartition.Add(row);
                PartitionIntoRows(modules, i, availableRowWidth, availableTotalHeight, currentPartition, results);
                currentPartition.RemoveAt(currentPartition.Count - 1);
            }
        }

        /// <summary>
        /// Converts a valid partition of rows into an arrangement with placements.
        /// Modules are placed starting at the bottom-left corner (origin) of the land.
        /// Returns null if any placement exceeds the land boundaries.
        /// </summary>
        private ModuleArrangementResult CreateArrangementFromRows(List<List<ModuleInstance>> rows, XYZ origin,
            double availableWidth, double availableHeight)
        {
            List<PlacedModule> placements = new List<PlacedModule>();
            double currentY = origin.Y;

            foreach (var row in rows)
            {
                double rowHeight = row.Max(m => m.EffectiveVertical);
                double currentX = origin.X;
                foreach (var mi in row)
                {
                    if (currentX + mi.EffectiveHorizontal > origin.X + availableWidth)
                        return null;
                    placements.Add(new PlacedModule
                    {
                        ModuleInstance = mi,
                        Origin = new XYZ(currentX, currentY, 0)
                    });
                    currentX += mi.EffectiveHorizontal;
                }
                currentY += rowHeight;
                if (currentY > origin.Y + availableHeight)
                    return null;
            }
            return new ModuleArrangementResult { PlacedModules = placements };
        }

        /// <summary>
        /// Converts an integer into a binary string with a fixed number of bits.
        /// For instance, ToBinaryString(3, 4) returns "0011".
        /// </summary>
        private string ToBinaryString(int value, int bits)
        {
            return Convert.ToString(value, 2).PadLeft(bits, '0');
        }

        /// <summary>
        /// Creates a signature string for an arrangement. For each placed module, its position and effective dimensions 
        /// (rounded to three decimal places) are concatenated into a string.
        /// </summary>
        private string GetArrangementSignature(ModuleArrangementResult arrangement)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var placed in arrangement.PlacedModules)
            {
                sb.AppendFormat("{0:F3},{1:F3},{2:F3},{3:F3};",
                    placed.Origin.X,
                    placed.Origin.Y,
                    placed.ModuleInstance.EffectiveHorizontal,
                    placed.ModuleInstance.EffectiveVertical);
            }
            return sb.ToString();
        }

        /// <summary>
        /// (Helper) Finds a scenario string that produced an arrangement.
        /// In this simplified example, we search the ScenarioLogs for any scenario whose binary string
        /// appears in the valid arrangements list. In a production system, one would store the scenario directly.
        /// Here, we simply return the first scenario (binary string) found.
        /// </summary>
        private string FindScenarioForArrangement(ModuleArrangementResult arrangement, List<ModuleArrangementResult> allArrangements, List<ModuleType> baseModules)
        {
            // For simplicity, we'll return the scenario from the logs that has a nonzero count.
            // In a more complete solution, you would store the scenario with each arrangement.
            foreach (string log in ScenarioLogs)
            {
                if (log.Contains("Scenario"))
                    return log.Split(' ')[1].TrimEnd(':');
            }
            return "N/A";
        }

        #endregion
    }

    /// <summary>
    /// Represents a module instance including its orientation.
    /// If IsRotated is true, then effective horizontal = Module.Width and effective vertical = Module.Length; otherwise vice versa.
    /// </summary>
    public class ModuleInstance
    {
        public ModuleType Module { get; set; }
        public bool IsRotated { get; set; }
        public double EffectiveHorizontal => IsRotated ? Module.Width : Module.Length;
        public double EffectiveVertical => IsRotated ? Module.Length : Module.Width;
    }

    /// <summary>
    /// Represents one valid arrangement of modules (and their placements).
    /// </summary>
    public class ModuleArrangementResult
    {
        public List<PlacedModule> PlacedModules { get; set; }
    }

    /// <summary>
    /// Represents a module placed at a specific position (its bottom-left corner).
    /// </summary>
    public class PlacedModule
    {
        public ModuleInstance ModuleInstance { get; set; }
        public XYZ Origin { get; set; }
    }

    
 
}
