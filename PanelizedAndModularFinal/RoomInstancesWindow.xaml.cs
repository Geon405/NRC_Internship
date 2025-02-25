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
using System.Windows;
using System.Windows.Controls;

namespace PanelizedAndModularFinal
{
    public partial class RoomInstancesWindow : Window
    {
        public List<RoomInstanceRow> Instances { get; set; }
        private const double MIN_AREA = 10.0; // Minimum allowed area per room

        public RoomInstancesWindow(List<RoomInstanceRow> instances)
        {
            InitializeComponent();
            Instances = instances;
            InstancesDataGrid.ItemsSource = Instances;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate all room areas before proceeding
            foreach (var instance in Instances)
            {
                if (instance.Area < MIN_AREA)
                {
                    MessageBox.Show($"Error: The room \"{instance.Name}\" must have an area of at least {MIN_AREA}ft².\n" +
                                    $"Please correct it before proceeding.",
                                    "Invalid Area", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return; // Stop the process, prevent closing
                }
            }

            this.DialogResult = true; // Proceed if validation passes
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}