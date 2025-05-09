using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace PanelizedAndModularFinal
{
    public partial class ArrangementSelectionWindow : Window
    {
        public ModuleArrangementResult SelectedArrangement { get; private set; }

        // Internal helper to bind into the ListBox
        private class DisplayItem
        {
            public ModuleArrangementResult Arrangement { get; }
            public string DisplayText { get; }

            public DisplayItem(ModuleArrangementResult arr, int index)
            {
                Arrangement = arr;

                // Reverse so bit[0] lines up with ModuleInstances[0]
                string bits = arr.OrientationStr;
                string displayBits = new string(bits.Reverse().ToArray());

                // Build a mapping "M<typeID>:<bit>"
                var sb = new StringBuilder();
                sb.Append($"#{index}: ");
                for (int j = 0; j < arr.ModuleInstances.Count; j++)
                {
                    var mi = arr.ModuleInstances[j];
                    // M<TypeID> : 0 or 1
                    sb.Append($"M{mi.Module.ID}:{displayBits[j]}");
                    if (j < arr.ModuleInstances.Count - 1)
                        sb.Append(", ");
                }

                DisplayText = sb.ToString();
            }
        }

        public ArrangementSelectionWindow(List<ModuleArrangementResult> uniqueArrangements)
        {
            InitializeComponent();

            // Set total module count
            if (uniqueArrangements.Any())
            {
                int moduleCount = uniqueArrangements[0].PlacedModules.Count;
                tbModuleCount.Text = $"Total modules: {moduleCount}";
            }

            // Populate list
            var items = uniqueArrangements
                .Select((arr, idx) => new DisplayItem(arr, idx + 1))
                .ToList();

            lbArrangements.ItemsSource = items;
            lbArrangements.SelectedIndex = 0;
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            var sel = lbArrangements.SelectedItem as DisplayItem;
            if (sel == null)
            {
                MessageBox.Show(
                  "Please select an arrangement.",
                  "Selection Required",
                  MessageBoxButton.OK,
                  MessageBoxImage.Warning
                );
                return;
            }
            SelectedArrangement = sel.Arrangement;
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
