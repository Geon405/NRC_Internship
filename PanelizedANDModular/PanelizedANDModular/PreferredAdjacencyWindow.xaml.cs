using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PanelizedAndModularFinal
{
    public partial class PreferredAdjacencyWindow : Window
    {
        // 2D matrix: 0 (no preferred adjacency) or 1 (preferred)
        public int[,] PreferredAdjacency { get; private set; }
        private List<SpaceNode> _spaces;

        public PreferredAdjacencyWindow(List<SpaceNode> spaces)
        {
            InitializeComponent();
            _spaces = spaces;
            int count = _spaces.Count;
            PreferredAdjacency = new int[count, count];

            // Initialize all to 0
            for (int i = 0; i < count; i++)
                for (int j = 0; j < count; j++)
                    PreferredAdjacency[i, j] = 0;

            SetupMatrixGrid();
        }

        private void SetupMatrixGrid()
        {
            int n = _spaces.Count;

            // Create first column for room name (read-only)
            DataGridTextColumn nameColumn = new DataGridTextColumn
            {
                Header = "Room",
                Binding = new Binding("RoomName"),
                IsReadOnly = true
            };
            MatrixGrid.Columns.Add(nameColumn);

            // Create one column per SpaceNode for selecting 0 or 1
            for (int col = 0; col < n; col++)
            {
                DataGridTemplateColumn colTemplate = new DataGridTemplateColumn
                {
                    Header = _spaces[col].Name
                };

                // Create editing template using a ComboBox with values 0 and 1
                FrameworkElementFactory comboFactory = new FrameworkElementFactory(typeof(ComboBox));
                comboFactory.SetValue(ComboBox.ItemsSourceProperty, new int[] { 0, 1 });
                comboFactory.SetBinding(ComboBox.SelectedValueProperty,
                    new Binding($"Values[{col}]")
                    {
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    });
                DataTemplate editingTemplate = new DataTemplate();
                editingTemplate.VisualTree = comboFactory;
                colTemplate.CellEditingTemplate = editingTemplate;

                // Create display template using a TextBlock to show the integer value
                DataTemplate displayTemplate = new DataTemplate();
                FrameworkElementFactory textFactory = new FrameworkElementFactory(typeof(TextBlock));
                textFactory.SetBinding(TextBlock.TextProperty, new Binding($"Values[{col}]"));
                displayTemplate.VisualTree = textFactory;
                colTemplate.CellTemplate = displayTemplate;

                MatrixGrid.Columns.Add(colTemplate);
            }

            // Create row data for each SpaceNode with default 0 values
            List<RowData> rows = new List<RowData>();
            for (int row = 0; row < n; row++)
            {
                rows.Add(new RowData
                {
                    RoomName = _spaces[row].Name,
                    Values = new int[n] // all zero by default
                });
            }

            MatrixGrid.ItemsSource = rows;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // Force any pending cell edits to commit
            MatrixGrid.CommitEdit(DataGridEditingUnit.Row, true);
            MatrixGrid.CommitEdit();

            var rows = MatrixGrid.ItemsSource as List<RowData>;
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
