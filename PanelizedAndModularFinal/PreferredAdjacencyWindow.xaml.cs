using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PanelizedAndModularFinal
{
    public partial class PreferredAdjacencyWindow : Window
    {
        // 2D adjacency matrix: 0 (no adjacency) or 1 (preferred adjacency)
        public int[,] PreferredAdjacency { get; private set; }
        private List<SpaceNode> _spaces;

        public PreferredAdjacencyWindow(List<SpaceNode> spaces)
        {
            InitializeComponent();
            _spaces = spaces;
            int count = _spaces.Count;
            PreferredAdjacency = new int[count, count];

            // Initialize all values to 0
            for (int i = 0; i < count; i++)
                for (int j = 0; j < count; j++)
                    PreferredAdjacency[i, j] = 0;

            SetupMatrixGrid();
        }
        private void SetupMatrixGrid()
        {
            int n = _spaces.Count;

            // Create first column for room names (read-only)
            DataGridTextColumn nameColumn = new DataGridTextColumn
            {
                Header = "Room",
                Binding = new Binding("RoomName"),
                IsReadOnly = true
            };
            MatrixGrid.Columns.Add(nameColumn);

            // Create adjacency selection columns
            for (int col = 0; col < n; col++)
            {
                DataGridComboBoxColumn comboColumn = new DataGridComboBoxColumn
                {
                    Header = _spaces[col].Name,
                    ItemsSource = new int[] { 0, 1 }, // Ensures user can select 0 or 1
                    SelectedItemBinding = new Binding($"Values[{col}]")
                    {
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    }
                };

                MatrixGrid.Columns.Add(comboColumn);
            }

            // Create row data for each space node with default 0 values
            List<RowData> rows = new List<RowData>();
            for (int row = 0; row < n; row++)
            {
                rows.Add(new RowData
                {
                    RoomName = _spaces[row].Name,
                    Values = new int[n] // Initialize all to 0
                });
            }

            MatrixGrid.ItemsSource = rows;

            // Attach event handler to ensure symmetry and commit changes
            MatrixGrid.CellEditEnding += MatrixGrid_CellEditEnding;
        }
        private void MatrixGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditingElement is ComboBox comboBox && e.Row.DataContext is RowData row)
            {
                int selectedIndex = MatrixGrid.Items.IndexOf(row);
                int columnIndex = e.Column.DisplayIndex - 1; // Adjust for the Room Name column

                if (columnIndex >= 0 && selectedIndex >= 0)
                {
                    // Get selected value from ComboBox
                    if (comboBox.SelectedItem is int selectedValue)
                    {
                        var rows = (List<RowData>)MatrixGrid.ItemsSource;

                        // Check if the value actually changed
                        if (rows[selectedIndex].Values[columnIndex] != selectedValue ||
                            rows[columnIndex].Values[selectedIndex] != selectedValue)
                        {
                            // Update values in the adjacency matrix
                            rows[selectedIndex].Values[columnIndex] = selectedValue;
                            rows[columnIndex].Values[selectedIndex] = selectedValue; // Mirror update

                            PreferredAdjacency[selectedIndex, columnIndex] = selectedValue;
                            PreferredAdjacency[columnIndex, selectedIndex] = selectedValue;

                            // Ensure UI updates correctly without causing recursion
                            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                MatrixGrid.ItemsSource = null; // Reset binding
                                MatrixGrid.ItemsSource = rows; // Rebind with updated data
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                }
            }
        }
        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // Ensure all pending edits are committed
            MatrixGrid.CommitEdit(DataGridEditingUnit.Row, true);
            MatrixGrid.CommitEdit();

            var rows = (List<RowData>)MatrixGrid.ItemsSource;
            for (int i = 0; i < rows.Count; i++)
            {
                for (int j = 0; j < rows[i].Values.Length; j++)
                {
                    PreferredAdjacency[i, j] = rows[i].Values[j];
                }
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Helper class for each row in the grid
        public class RowData
        {
            public string RoomName { get; set; }
            public int[] Values { get; set; }
        }
    }
}