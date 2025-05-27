using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PanelizedAndModularFinal
{
    public partial class ModuleCombinationsWindow : Window
    {
        // After OK, this will hold the chosen combination.
        public string SelectedCombination { get; private set; }

        // Penalty weights
        const double w1 = 0.6, w2 = 0.3, w3 = 0.1;

        // The “best” combos, sorted by penalty
        private List<(Combination combo, double totalArea, double penalty)> _bestList;

        public ModuleCombinationsWindow(List<ModuleType> moduleTypes, double minWidth)
        {
            InitializeComponent();

            // 1) Compute area bounds
            double maxBuildingSize = 0.4 * GlobalData.LandArea;
            double maxTotalSpaceSize = maxBuildingSize - 0.15 * maxBuildingSize;
            double lowerBound = maxTotalSpaceSize;
            double upperBound = maxBuildingSize;
            lblAreaInfo.Content = $"Minimum Area: {lowerBound:F2} ft², Maximum Area: {upperBound:F2} ft²";

            // 2) Enumerate ALL raw combinations in [lowerBound, upperBound]
            var combinations = new List<Combination>();
            int[] counts = new int[moduleTypes.Count];
            int maxModules = (int)Math.Ceiling(upperBound / moduleTypes.Min(mt => mt.Area));
            FindCombinations(
                moduleTypes,
                startIndex: 0,
                modulesUsed: 0,
                currentSum: 0.0,
                counts: counts,
                maxModules: maxModules,
                lowerBound: lowerBound,
                upperBound: upperBound,
                results: combinations
            );

            // 3) Bail if none found
            if (!combinations.Any())
            {
                MessageBox.Show("No valid module combinations found!",
                                "Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                DialogResult = false;
                Close();
                return;
            }

            // 4) Compute MC (#modules) and DS (#distinct types) for each
            var infos = combinations
                .Select(c => new {
                    Combo = c,
                    TotalArea = c.TotalArea,
                    MC = c.ModuleCounts.Values.Sum(),
                    DS = c.ModuleCounts.Count
                })
                .ToList();

            int minMC = infos.Min(i => i.MC),
                maxMC = infos.Max(i => i.MC),
                minDS = infos.Min(i => i.DS),
                maxDS = infos.Max(i => i.DS);

            double requiredArea = GlobalData.TotalRoomArea;

            // 5) Score each combo
            var scored = infos
                .Select(i =>
                {
                    double aer = requiredArea / i.TotalArea;
                    double pAER = Math.Pow(1 - aer, 2);
                    double pMC = (i.MC - minMC) / (double)(maxMC - minMC);
                    double pDS = (i.DS - minDS) / (double)(maxDS - minDS);
                    double tot = w1 * pAER + w2 * pMC + w3 * pDS;
                    return (combo: i.Combo, totalArea: i.TotalArea, penalty: tot);
                })
                .ToList();

            // 6) Keep those < 0.1, or else the absolute lowest‐penalty
            var best = scored.Where(x => x.penalty < 0.1).ToList();
            if (!best.Any())
            {
                double minPen = scored.Min(x => x.penalty);
                best = scored
                    .Where(x => Math.Abs(x.penalty - minPen) < 1e-6)
                    .ToList();
            }

            // 7) Order by penalty, stash in _bestList
            _bestList = best
                .OrderBy(x => x.penalty)
                .ToList();

            // 8) Let the user inspect
            lbCombinations.ItemsSource = _bestList
                .Select(x =>
                {
                    var parts = x.combo.ModuleCounts
                                .OrderBy(kv => kv.Key)
                                .Select(kv => $"{kv.Value} × Module_Type {kv.Key + 1}");
                    string comboText = string.Join(" + ", parts);
                    return $"{comboText,-30} = {x.totalArea,6:F0}ft² → Penalty: {x.penalty:F4}";
                })
                .ToList();
        }

        /// <summary>
        /// Fires when the user clicks OK:
        /// we simply pick the first (lowest‐penalty) combo.
        /// </summary>
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_bestList == null || !_bestList.Any())
            {
                MessageBox.Show("No combinations to select.",
                                "Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                DialogResult = false;
            }
            else
            {
                var pick = _bestList[0];
                var parts = pick.combo.ModuleCounts
                               .OrderBy(kv => kv.Key)
                               .Select(kv => $"{kv.Value} x Module_Type {kv.Key + 1}");
                SelectedCombination = $"{string.Join(" + ", parts)} = {pick.totalArea:F0} ft²";
                DialogResult = true;
            }

            Close();
        }

        /// <summary>
        /// Recursively builds up every module‐count combination whose
        /// total area lies between lowerBound and upperBound.
        /// </summary>
        private void FindCombinations(
            List<ModuleType> moduleTypes,
            int startIndex,
            int modulesUsed,
            double currentSum,
            int[] counts,
            int maxModules,
            double lowerBound,
            double upperBound,
            List<Combination> results)
        {
            if (modulesUsed > 0
                && currentSum >= lowerBound
                && currentSum <= upperBound)
            {
                var combo = new Combination
                {
                    TotalArea = currentSum,
                    ModuleCounts = new Dictionary<int, int>()
                };
                for (int i = 0; i < counts.Length; i++)
                    if (counts[i] > 0)
                        combo.ModuleCounts[i] = counts[i];
                results.Add(combo);
            }

            if (modulesUsed == maxModules || currentSum > upperBound)
                return;

            for (int i = startIndex; i < moduleTypes.Count; i++)
            {
                counts[i]++;
                FindCombinations(
                    moduleTypes,
                    i,
                    modulesUsed + 1,
                    currentSum + moduleTypes[i].Area,
                    counts,
                    maxModules,
                    lowerBound,
                    upperBound,
                    results
                );
                counts[i]--;
            }
        }
    }
}
