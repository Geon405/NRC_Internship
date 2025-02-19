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
using System;
using System.Collections.Generic;
using static PanelizedAndModularFinal.ConnectivityMatrixWindow;


namespace PanelizedAndModularFinal
{
    public partial class EdgeWeightsWindow : Window
    {
        // This will hold the final user-entered weights.
        public double?[,] WeightedAdjacencyMatrix { get; private set; }

        private List<SpaceNode> _spaces;
        private int[,] _adjacency;

        public EdgeWeightsWindow(List<SpaceNode> spaces, int[,] adjacencyMatrix)
        {
            InitializeComponent();
            _spaces = spaces;
            _adjacency = adjacencyMatrix;

            int n = _spaces.Count;
            WeightedAdjacencyMatrix = new double?[n, n];

            // Prepare rows for display: each connected pair (i < j) with adjacency=1
            List<EdgeWeightRow> rows = new List<EdgeWeightRow>();
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (_adjacency[i, j] == 1)
                    {
                        rows.Add(new EdgeWeightRow
                        {
                            EdgeDescription = $"{_spaces[i].Name} - {_spaces[j].Name}",
                            I = i,
                            J = j,
                            Weight = 1.0 // Default or empty
                        });
                    }
                }
            }

            EdgeWeightsGrid.ItemsSource = rows;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // Read the user inputs back into WeightedAdjacencyMatrix
            var rows = (List<EdgeWeightRow>)EdgeWeightsGrid.ItemsSource;
            foreach (var row in rows)
            {
                WeightedAdjacencyMatrix[row.I, row.J] = row.Weight;
                WeightedAdjacencyMatrix[row.J, row.I] = row.Weight; // Symmetric
            }
            DialogResult = true;
            Close();
        }
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    // Simple row model for binding in the DataGrid
    public class EdgeWeightRow
    {
        public string EdgeDescription { get; set; }
        public int I { get; set; }
        public int J { get; set; }
        public double? Weight { get; set; }
    }
}
