using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.UI;

namespace PanelizedAndModularFinal
{
    public partial class ModuleCombinationsWindow : Window
    {
        // This property will contain the selected combination string.
        public string SelectedCombination { get; private set; }

        // Penalty weights
        const double w1 = 0.6;
        const double w2 = 0.3;
        const double w3 = 0.1;

        public ModuleCombinationsWindow(List<ModuleType> moduleTypes, double minWidth)
        {
            InitializeComponent();

            // same bounds as before
            double maxBuildingSize = 0.6 * GlobalData.LandArea;
            double maxTotalSpaceSize = maxBuildingSize - (0.15 * maxBuildingSize);
            double lowerBound = maxTotalSpaceSize;
            double upperBound = maxBuildingSize;

            lblAreaInfo.Content = $"Minimum Area: {lowerBound:F2} ft², Maximum Area: {upperBound:F2} ft²";

            // 1) Generate all raw combinations
            List<Combination> combinations = new List<Combination>();
            int[] counts = new int[moduleTypes.Count];
            FindCombinations(
                moduleTypes, 0, 0, 0,
                counts, (int)Math.Ceiling(upperBound / moduleTypes.Min(mt => mt.Area)),
                lowerBound, upperBound,
                combinations
            );

            // 2) Precompute MC and DS for each
            var comboInfos = combinations
                .Select(c => new
                {
                    Combo = c,
                    TotalArea = c.TotalArea,
                    MC = c.ModuleCounts.Values.Sum(),
                    DS = c.ModuleCounts.Count   // only non-zero types were added
                })
                .ToList();

            // 3) Find the min/max for MC and DS
            int minMC = comboInfos.Min(ci => ci.MC),
                maxMC = comboInfos.Max(ci => ci.MC);
            int minDS = comboInfos.Min(ci => ci.DS),
                maxDS = comboInfos.Max(ci => ci.DS);

            // 4) Required area is the sum of room areas from earlier step
            double requiredArea = GlobalData.TotalRoomArea;

            // 5) Compute penalties
            var scored = comboInfos.Select(ci =>
            {
                // AER penalty
                double aer = requiredArea / ci.TotalArea;
                double pAER = Math.Pow(1 - aer, 2);

                // Module count penalty
                double pMC = (ci.MC - minMC) / (double)(maxMC - minMC);

                // Diversity of sizes penalty
                double pDS = (ci.DS - minDS) / (double)(maxDS - minDS);

                double totalPenalty = w1 * pAER + w2 * pMC + w3 * pDS;

                return new
                {
                    ci.Combo,
                    ci.TotalArea,
                    ci.MC,
                    ci.DS,
                    Penalty = totalPenalty
                };
            }).ToList();

            // 6) Filter those under 0.1 or else pick the absolute lowest
            var best = scored.Where(x => x.Penalty < 0.1).ToList();
            if (!best.Any())
            {
                double minPenalty = scored.Min(x => x.Penalty);
                best = scored.Where(x => Math.Abs(x.Penalty - minPenalty) < 1e-6).ToList();
            }

            // 7) Build display strings
            var displayList = best
                .Select(x =>
                {
                    // Reconstruct the combination string
                    string combStr = String.Join(" + ",
                        x.Combo.ModuleCounts
                         .OrderBy(kv => kv.Key)
                         .Select(kv => $"{kv.Value} x Module_Type {kv.Key + 1}")
                    );
                    return $"{combStr} = {x.TotalArea:F0} ft²  ➔  Penalty = {x.Penalty:F4}";
                })
                .ToList();

            lbCombinations.ItemsSource = displayList;
        }

        // Recursive backtracking to find all combinations.
        private void FindCombinations(List<ModuleType> moduleTypes, int startIndex, int modulesUsed, double currentSum, int[] counts,
            int maxModules, double lowerBound, double upperBound, List<Combination> results)
        {
            if (modulesUsed > 0 && currentSum >= lowerBound && currentSum <= upperBound)
            {
                // Record the current combination.
                var combo = new Combination
                {
                    TotalArea = currentSum,
                    ModuleCounts = new Dictionary<int, int>()
                };
                for (int i = 0; i < counts.Length; i++)
                {
                    if (counts[i] > 0)
                        combo.ModuleCounts[i] = counts[i];
                }
                results.Add(combo);
            }
            if (modulesUsed == maxModules || currentSum > upperBound)
                return;

            for (int i = startIndex; i < moduleTypes.Count; i++)
            {
                counts[i]++;
                FindCombinations(moduleTypes, i, modulesUsed + 1, currentSum + moduleTypes[i].Area, counts, maxModules, lowerBound, upperBound, results);
                counts[i]--;
            }
        }

        private void btnSelect_Click(object sender, RoutedEventArgs e)
        {
            if (lbCombinations.SelectedItem == null)
            {
                MessageBox.Show("Please select a combination.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SelectedCombination = lbCombinations.SelectedItem.ToString();
            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}