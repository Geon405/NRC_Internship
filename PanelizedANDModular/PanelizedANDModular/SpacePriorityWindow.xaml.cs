using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PanelizedAndModularFinal
{
    public partial class SpacePriorityWindow : Window
    {
        private List<SpaceNode> _spaces;

        public SpacePriorityWindow(List<SpaceNode> spaces)
        {
            InitializeComponent();
            _spaces = spaces;
            PrioritiesDataGrid.ItemsSource = _spaces;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Commit any pending edits.
            PrioritiesDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            PrioritiesDataGrid.CommitEdit();

            double total = _spaces.Sum(s => s.Priority);
            if (total <= 0)
            {
                MessageBox.Show("Please enter positive raw priority values for all spaces.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Normalize each space's priority.
            foreach (var space in _spaces)
            {
                space.Priority = space.Priority / total;
            }

            this.DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }
    }
}
