using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PanelizedAndModularFinal
{
    /// <summary>
    /// Interaction logic for ArrangementSelectionWindow.xaml
    /// </summary>
    public partial class ArrangementSelectionWindow : Window
    {
        // Public property to retrieve the user’s choice.
        public ModuleArrangementResult SelectedArrangement { get; private set; }

        // Internal helper to bind into the ListBox
        private class DisplayItem
        {
            public ModuleArrangementResult Arrangement { get; }
            public string DisplayText { get; }

            public DisplayItem(ModuleArrangementResult arr, int index)
            {
                Arrangement = arr;

                // Reverse the bit‐string so bit[0] matches ModuleInstances[0], etc.
                string bits = arr.OrientationStr;
                string displayBits = new string(bits.Reverse().ToArray());

                DisplayText = $"#{index}: Bits={displayBits}, Modules={arr.PlacedModules.Count}";
            }
        }

        public ArrangementSelectionWindow(List<ModuleArrangementResult> uniqueArrangements)
        {
            InitializeComponent();

            // Build display items
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
                MessageBox.Show("Please select an arrangement.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
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
