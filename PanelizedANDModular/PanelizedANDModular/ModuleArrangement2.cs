using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PanelizedAndModularFinal
{
    /// <summary>
    /// ModuleArrangement2 arranges modules using every possible orientation assignment (2^n scenarios),
    /// then places them inside the boundary by recursive adjacency backtracking,
    /// collecting ALL valid adjacency-based layouts.
    /// </summary>
    public class ModuleArrangement2
    {
        private List<ModuleType> _moduleTypes;
        private string _selectedCombination;
        private BoundingBoxXYZ _cropBox;

        public List<string> ScenarioLogs { get; private set; }

        public ModuleArrangement2(List<ModuleType> moduleTypes, string selectedCombination, BoundingBoxXYZ cropBox)
        {
            _moduleTypes = moduleTypes;
            _selectedCombination = selectedCombination;
            _cropBox = cropBox;
        }

        public List<ModuleArrangementResult> GetValidArrangements()
        {
            // 1) Unroll your counts into a flat list
            var typeCounts = ParseCombinationString(_selectedCombination);
            var flat = new List<ModuleType>();
            foreach (var kv in typeCounts)
                for (int i = 0; i < kv.Value; i++)
                    flat.Add(_moduleTypes[kv.Key]);

            int n = flat.Count;
            int totalScenarios = 1 << n;
            var allResults = new List<ModuleArrangementResult>();
            ScenarioLogs = new List<string>();

            // 2) For each orientation bitmask
            for (int mask = 0; mask < totalScenarios; mask++)
            {
                string bits = ToBinaryString(mask, n);

                // 2a) Build the oriented instances in flat order
                var baseInstances = new List<ModuleInstance>();
                for (int i = 0; i < n; i++)
                {
                    bool rot = ((mask >> i) & 1) == 1;
                    baseInstances.Add(new ModuleInstance
                    {
                        Module = flat[i],
                        IsRotated = rot
                    });
                }

                // 2b) Permute those n instances into every possible placement order
                var allOrders = Permute(baseInstances);

                var layoutsThisScenario = new List<List<PlacedModule>>();
                // 3) For each ordering, collect all adjacency‐based packings
                foreach (var order in allOrders)
                    CollectLayouts(order, layoutsThisScenario);

                // 4) Log how many layouts we found under this bitmask
                ScenarioLogs.Add(
                    $"Scenario {bits}: {layoutsThisScenario.Count} valid layout(s)");

                // 5) Push them all into the big results list
                foreach (var layout in layoutsThisScenario)
                {
                    allResults.Add(new ModuleArrangementResult
                    {
                        PlacedModules = layout,
                        OrientationStr = bits,
                        ModuleInstances = new List<ModuleInstance>(baseInstances)
                    });
                }
            }

            // 6) Finally dedupe identical layouts
            var unique = allResults
              .GroupBy(a => string.Join("|", a.PlacedModules
                 .Select(pm => $"{pm.Origin.X:F2},{pm.Origin.Y:F2}," +
                                $"{pm.ModuleInstance.EffectiveHorizontal:F2}," +
                                $"{pm.ModuleInstance.EffectiveVertical:F2}")))
              .Select(g => g.First())
              .ToList();

            return unique;
        }


        // Helper to generate all permutations of a list
        private IEnumerable<List<T>> Permute<T>(List<T> list)
        {
            if (list.Count == 1)
                yield return new List<T>(list);
            else
            {
                for (int i = 0; i < list.Count; i++)
                {
                    // pick element i
                    T elem = list[i];
                    var remainder = new List<T>(list);
                    remainder.RemoveAt(i);

                    foreach (var perm in Permute(remainder))
                    {
                        var result = new List<T> { elem };
                        result.AddRange(perm);
                        yield return result;
                    }
                }
            }
        }


        private void CollectLayouts(List<ModuleInstance> insts, List<List<PlacedModule>> layouts)
        {
            var placed = new List<PlacedModule>();
            var first = insts[0];

            // compute its bottom-left so its top-edge touches cropBox.Max.Y
            double x0 = _cropBox.Min.X;
            double y0 = _cropBox.Max.Y - first.EffectiveVertical;

            // NEW: reject if it wouldn’t lie entirely inside the boundary
            double availW = _cropBox.Max.X - _cropBox.Min.X;
            double availH = _cropBox.Max.Y - _cropBox.Min.Y;
            if (first.EffectiveHorizontal > availW || first.EffectiveVertical > availH)
            {
                // no layout possible with this orientation
                return;
            }

            placed.Add(new PlacedModule { ModuleInstance = first, Origin = new XYZ(x0, y0, 0) });
            PlaceNextAll(insts, placed, 1, layouts);
        }

        private void PlaceNextAll(List<ModuleInstance> insts, List<PlacedModule> placed, int idx, List<List<PlacedModule>> layouts)
        {
            if (idx >= insts.Count)
            {
                layouts.Add(placed.Select(pm => new PlacedModule
                {
                    ModuleInstance = pm.ModuleInstance,
                    Origin = pm.Origin
                }).ToList());
                return;
            }
            var cur = insts[idx];
            var boundary = _cropBox;
            foreach (var pm in placed.ToList())
            {
                var baseX = pm.Origin.X;
                var baseY = pm.Origin.Y;
                double w1 = pm.ModuleInstance.EffectiveHorizontal;
                double h1 = pm.ModuleInstance.EffectiveVertical;
                double w2 = cur.EffectiveHorizontal;
                double h2 = cur.EffectiveVertical;
                var candidates = new List<XYZ> {
                    new XYZ(baseX + w1, baseY, 0),
                    new XYZ(baseX - w2, baseY, 0),
                    new XYZ(baseX, baseY + h1, 0),
                    new XYZ(baseX, baseY - h2, 0)
                };
                foreach (var o in candidates)
                {
                    if (o.X < boundary.Min.X || o.Y < boundary.Min.Y) continue;
                    if (o.X + w2 > boundary.Max.X || o.Y + h2 > boundary.Max.Y) continue;
                    var rect2 = Tuple.Create(o, w2, h2);
                    if (IsOverlapping(rect2, placed)) continue;
                    if (!SharesSide(rect2, placed)) continue;
                    placed.Add(new PlacedModule { ModuleInstance = cur, Origin = o });
                    PlaceNextAll(insts, placed, idx + 1, layouts);
                    placed.RemoveAt(placed.Count - 1);
                }
            }
        }

        private bool IsOverlapping(Tuple<XYZ, double, double> rect, List<PlacedModule> placed)
        {
            double x0 = rect.Item1.X, y0 = rect.Item1.Y;
            double w = rect.Item2, h = rect.Item3;
            foreach (var pm in placed)
            {
                double x1 = pm.Origin.X, y1 = pm.Origin.Y;
                double w1 = pm.ModuleInstance.EffectiveHorizontal;
                double h1 = pm.ModuleInstance.EffectiveVertical;
                if (x0 < x1 + w1 && x0 + w > x1 && y0 < y1 + h1 && y0 + h > y1)
                    return true;
            }
            return false;
        }

        private bool SharesSide(Tuple<XYZ, double, double> rect, List<PlacedModule> placed)
        {
            double x0 = rect.Item1.X, y0 = rect.Item1.Y;
            double w = rect.Item2, h = rect.Item3;
            foreach (var pm in placed)
            {
                double x1 = pm.Origin.X, y1 = pm.Origin.Y;
                double w1 = pm.ModuleInstance.EffectiveHorizontal;
                double h1 = pm.ModuleInstance.EffectiveVertical;
                if (Math.Abs(x0 + w - x1) < 1e-6 || Math.Abs(x1 + w1 - x0) < 1e-6)
                {
                    if (y0 < y1 + h1 && y0 + h > y1) return true;
                }
                if (Math.Abs(y0 + h - y1) < 1e-6 || Math.Abs(y1 + h1 - y0) < 1e-6)
                {
                    if (x0 < x1 + w1 && x0 + w > x1) return true;
                }
            }
            return false;
        }




        /// <summary>
        /// Shows which orientation scenarios (bit‐strings) produced the final unique arrangements,
        /// and how many unique arrangements there are in total.
        /// 0 means “not rotated”  Horizontal(along land width) = module length  Vertical(along land length) = module width
        /// 1 means “rotated” Horizontal = module width Vertical = module length
        /// </summary>
        public void DisplayScenarioSummary(List<ModuleArrangementResult> uniqueArrangements)
        {
            int totalComb = ScenarioLogs.Count;
            int uniqueCount = uniqueArrangements.Count;
            var lines = new List<string>();

            lines.Add($"Out of {totalComb} combos, found {uniqueCount} unique arrangements.");
            lines.Add("");
            //lines.Add("Legend: 0 = Normal (length→horiz), 1 = Rotated (width→horiz)");
            //lines.Add(new string('-', 60));

            //for (int i = 0; i < uniqueArrangements.Count; i++)
            //{
            //    var arr = uniqueArrangements[i];
            //    // Reverse the bits so they line up with ModuleInstances[0]..[n-1]
            //    string bits = arr.OrientationStr;
            //    string displayBits = new string(bits.Reverse().ToArray());

            //    lines.Add($"Arrangement #{i + 1}: Bits {displayBits}");
            //    lines.Add($"{"Bit",3}  {"ModuleType",-12}  {"Ori"}");
            //    lines.Add($"{"---",3}  {"------------",-12}  {"---"}");

            //    for (int j = 0; j < arr.ModuleInstances.Count; j++)
            //    {
            //        var mi = arr.ModuleInstances[j];
            //        var ori = mi.IsRotated ? "Rotated" : "Normal";
            //        var mt = $"Type{mi.Module.ID}";
            //        lines.Add($"{j + 1,3}  {mt,-12}  {ori}");
            //    }
            //    lines.Add("");
            //}

            TaskDialog.Show("Unique Arrangements Summary", string.Join("\n", lines));
        }



        /// <summary>
        /// After GetValidArrangements, call this to show how many unique layouts you have.
        /// </summary>
        public void DisplayUniqueCount(List<ModuleArrangementResult> uniqueArrangements)
        {
            TaskDialog.Show(
                "Unique Arrangements",
                $"Found {uniqueArrangements.Count} unique valid arrangement" +
                (uniqueArrangements.Count == 1 ? "" : "s") + "."
            );
        }




        /// <summary>
        /// Draws the modules for a given arrangement on the active Revit view.
        /// Each module is drawn as a red rectangle. This method returns a list of the ElementIds
        /// of the drawn detail curves, so they can later be removed.
        /// </summary>
        public List<ElementId> DrawArrangement(Document doc, ModuleArrangementResult arrangement)
        {
            List<ElementId> drawnElementIds = new List<ElementId>();

            // Prepare a red line override
            OverrideGraphicSettings ogs = new OverrideGraphicSettings()
                .SetProjectionLineColor(new Autodesk.Revit.DB.Color(255, 0, 0));

            using (Transaction t = new Transaction(doc, "Draw Modules"))
            {
                t.Start();
                View view = doc.ActiveView;

                foreach (PlacedModule pm in arrangement.PlacedModules)
                {
                    double w = pm.ModuleInstance.EffectiveHorizontal;
                    double h = pm.ModuleInstance.EffectiveVertical;

                    // Compute corners
                    XYZ p0 = new XYZ(pm.Origin.X, pm.Origin.Y, 0);
                    XYZ p1 = new XYZ(pm.Origin.X + w, pm.Origin.Y, 0);
                    XYZ p2 = new XYZ(pm.Origin.X + w, pm.Origin.Y + h, 0);
                    XYZ p3 = new XYZ(pm.Origin.X, pm.Origin.Y + h, 0);

                    // Draw each edge and override its color
                    var lines = new[] {
                Line.CreateBound(p0, p1),
                Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p0)
            };

                    foreach (var ln in lines)
                    {
                        // create curve
                        DetailCurve dc = doc.Create.NewDetailCurve(view, ln);
                        drawnElementIds.Add(dc.Id);

                        // apply red override
                        view.SetElementOverrides(dc.Id, ogs);
                    }
                }

                t.Commit();
            }

            return drawnElementIds;
        }


        #region Helper Methods
        // Parses a combination string such as "2 x Module_Type 1 + 1 x Module_Type 3 = ..." into a dictionary.
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
        /// Draws a square grid inside each placed module of the given arrangement,
        /// where each cell is (module vertical size ÷ 3) on a side.
        /// Returns the DetailCurve ElementIds, and outputs the list of cell areas (all identical = cellSize²).
        /// </summary>
        public List<ElementId> DrawModuleGrids(
     Document doc,
     ModuleArrangementResult arrangement,
     out List<double> cellAreas)
        {
            var gridIds = new List<ElementId>();
            cellAreas = new List<double>();

            // All modules share the same “module width” (vertical size when not rotated):
            double moduleWidth = arrangement
                .PlacedModules[0]
                .ModuleInstance
                .Module
                .Width;

            // Grid cell is always one‑third of that width:
            double cellSize = moduleWidth / 3.0;
            double cellArea = cellSize * cellSize;

            // Prepare blue line override
            var ogs = new OverrideGraphicSettings()
                .SetProjectionLineColor(new Autodesk.Revit.DB.Color(0, 0, 255));

            double tol = doc.Application.ShortCurveTolerance;
            var view = doc.ActiveView;

            using (var t = new Transaction(doc, "Draw Module Grids"))
            {
                t.Start();

                foreach (var pm in arrangement.PlacedModules)
                {
                    // Bounds of this module
                    double minX = pm.Origin.X;
                    double minY = pm.Origin.Y;
                    double maxX = minX + pm.ModuleInstance.EffectiveHorizontal;
                    double maxY = minY + pm.ModuleInstance.EffectiveVertical;

                    // How many columns & rows of cells fit
                    int nCols = (int)Math.Floor((maxX - minX) / cellSize);
                    int nRows = (int)Math.Floor((maxY - minY) / cellSize);

                    for (int i = 0; i < nCols; i++)
                    {
                        for (int j = 0; j < nRows; j++)
                        {
                            double x0 = minX + i * cellSize;
                            double y0 = minY + j * cellSize;
                            double x1 = x0 + cellSize;
                            double y1 = y0 + cellSize;

                            var corners = new[]
                            {
                        new XYZ(x0, y0, 0),
                        new XYZ(x1, y0, 0),
                        new XYZ(x1, y1, 0),
                        new XYZ(x0, y1, 0)
                    };

                            // Draw the square cell
                            for (int e = 0; e < 4; e++)
                            {
                                var ln = Line.CreateBound(corners[e], corners[(e + 1) % 4]);
                                if (ln.Length < tol) continue;
                                var dc = doc.Create.NewDetailCurve(view, ln);
                                view.SetElementOverrides(dc.Id, ogs);
                                gridIds.Add(dc.Id);
                            }

                            // Record the area of this cell
                            cellAreas.Add(cellArea);
                        }
                    }
                }

                t.Commit();
            }

            return gridIds;
        }





        // Recursively partitions the list of module instances into rows.
        // Each row's total effective horizontal dimensions (sum) must not exceed availableRowWidth,
        // and the total height (sum of the max effective vertical in each row) must not exceed availableTotalHeight.
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

        // Converts a valid partition of rows into an arrangement with explicit placements.
        // Modules are placed starting at the bottom-left corner (origin) of the land.
        // Returns null if any placement would exceed the land boundaries.
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

        // Converts an integer to a binary string with a fixed number of bits.
        private string ToBinaryString(int value, int bits)
        {
            return Convert.ToString(value, 2).PadLeft(bits, '0');
        }
        #endregion
    }

    /// <summary>
    /// Represents one module instance including its orientation.
    /// IsRotated true means that the module is flipped.
    /// For a non-rotated module, effective horizontal = Module.Length and effective vertical = Module.Width;
    /// for a rotated module, these values swap.
    /// </summary>
    public class ModuleInstance
    {
        public ModuleType Module { get; set; }
        public bool IsRotated { get; set; }
        public double EffectiveHorizontal => IsRotated ? Module.Width : Module.Length;
        public double EffectiveVertical => IsRotated ? Module.Length : Module.Width;
    }

    /// <summary>
    /// Represents one valid arrangement of modules with their placements.
    /// </summary>
    public class ModuleArrangementResult
    {
        public List<PlacedModule> PlacedModules { get; set; }
        public string OrientationStr { get; set; }

        public List<ModuleInstance> ModuleInstances { get; set; }
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
