using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Autodesk.Revit.DB;

namespace PanelizedAndModularFinal
{
    public partial class ConnectivityMatrixWindow : Window
    {
        // 2D adjacency matrix: 0 or 1

       
        public int[,] ConnectivityMatrix { get; private set; }

        private List<SpaceNode> _spaces;

        public ConnectivityMatrixWindow(List<SpaceNode> spaces)
        {
            InitializeComponent();
            _spaces = spaces;

            int count = _spaces.Count;
            ConnectivityMatrix = new int[count, count];

            // Initialize all to 0
            for (int i = 0; i < count; i++)
                for (int j = 0; j < count; j++)
                    ConnectivityMatrix[i, j] = 0;

            SetupMatrixGrid();
        }

        private void SetupMatrixGrid()
        {
            int n = _spaces.Count;
           
          

            // Create columns: first column for room name, then one column per space for 0/1
            DataGridTextColumn nameColumn = new DataGridTextColumn
            {
                Header = "Room",
                Binding = new System.Windows.Data.Binding("RoomName"),
                IsReadOnly = true
            };
            MatrixGrid.Columns.Add(nameColumn);

            for (int col = 0; col < n; col++)
            {
                // Create a column that binds to an index in row data
                DataGridTemplateColumn colTemplate = new DataGridTemplateColumn
                {
                    Header = _spaces[col].Name
                };

                // Cell editing template
                FrameworkElementFactory comboFactory = new FrameworkElementFactory(typeof(ComboBox));
                comboFactory.SetValue(ComboBox.ItemsSourceProperty, new int[] { 0, 1 });
                // Bind selected value to a property "Values[col]"
                comboFactory.SetBinding(ComboBox.SelectedValueProperty,
                    new System.Windows.Data.Binding($"Values[{col}]")
                    {
                        Mode = System.Windows.Data.BindingMode.TwoWay,
                        UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                    });

                DataTemplate editingTemplate = new DataTemplate();
                editingTemplate.VisualTree = comboFactory;
                colTemplate.CellEditingTemplate = editingTemplate;

                // For display, just show the integer
                DataGridTextColumn textDisplay = new DataGridTextColumn
                {
                    Binding = new System.Windows.Data.Binding($"Values[{col}]")
                };
                colTemplate.CellTemplate = new DataTemplate() { VisualTree = new FrameworkElementFactory(typeof(TextBlock)) };
                colTemplate.CellTemplate.VisualTree.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding($"Values[{col}]"));

                MatrixGrid.Columns.Add(colTemplate);
            }

            // Create row data
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

        //private void OK_Click(object sender, RoutedEventArgs e)
        //{
        //    // On OK, read the grid back into ConnectivityMatrix
        //    var rows = (List<RowData>)MatrixGrid.ItemsSource;
        //    for (int i = 0; i < rows.Count; i++)
        //    {
        //        for (int j = 0; j < rows[i].Values.Length; j++)
        //        {
        //            ConnectivityMatrix[i, j] = rows[i].Values[j];
        //        }
        //    }
        //    this.DialogResult = true;
        //    Close();
        //}

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            var rows = (List<RowData>)MatrixGrid.ItemsSource;
            for (int i = 0; i < rows.Count; i++)
            {
                for (int j = 0; j < rows[i].Values.Length; j++)
                {
                    ConnectivityMatrix[i, j] = rows[i].Values[j];
                    // Force symmetry for an undirected connection
                    ConnectivityMatrix[j, i] = rows[i].Values[j];
                }
            }
            this.DialogResult = true;
            Close();
        }


        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
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
