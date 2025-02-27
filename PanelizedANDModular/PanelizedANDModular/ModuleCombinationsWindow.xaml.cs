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

        public ModuleCombinationsWindow(List<ModuleType> moduleTypes, double minWidth)
        {
            InitializeComponent();

            // Check that the required area does not exceed 60% of the land area.
            if (GlobalData.TotalRoomArea > 0.6 * GlobalData.LandArea)
            {
                MessageBox.Show("Required area exceeds 60% of Land Area.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            double requiredArea = GlobalData.TotalRoomArea;
            double variance = minWidth * minWidth; // x*x
            double lowerBound = requiredArea - variance;
            double upperBound = requiredArea + variance;

            // Determine an upper bound for the number of modules (using the smallest module area).
            double smallestArea = moduleTypes.Min(mt => mt.Area);
            int maxModules = (int)Math.Ceiling(upperBound / smallestArea);

            // Display minimum and maximum area at the top.
            lblAreaInfo.Content = $"Minimum Area: {lowerBound} ft², Maximum Area: {upperBound} ft²";


            // Generate valid combinations.
            List<Combination> combinations = new List<Combination>();
            int[] counts = new int[moduleTypes.Count];
            FindCombinations(moduleTypes, 0, 0, 0, counts, maxModules, lowerBound, upperBound, combinations);

            // Sort combinations by TotalArea (from smallest to largest)
            combinations = combinations.OrderBy(c => c.TotalArea).ToList();

            // Convert each combination into a string for display.
            List<string> displayList = new List<string>();
            foreach (var comb in combinations)
            {
                string combStr = "";
                foreach (var kvp in comb.ModuleCounts)
                {
                    if (kvp.Value > 0)
                    {
                        // ModuleType indices start at 0, so add 1 for display.
                        combStr += $"{kvp.Value} x Module_Type {kvp.Key + 1} + ";
                    }
                }
                if (combStr.EndsWith(" + "))
                    combStr = combStr.Substring(0, combStr.Length - 3);
                combStr += $" = {comb.TotalArea} ft²";
                displayList.Add(combStr);
            }

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
