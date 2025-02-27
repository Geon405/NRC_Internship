using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PanelizedAndModularFinal
{
    public partial class RoomInstancesWindow : Window
    {
        public List<RoomInstanceRow> Instances { get; set; }



        public RoomInstancesWindow(List<RoomInstanceRow> instances)
        {
            InitializeComponent();
            Instances = instances;
            InstancesDataGrid.ItemsSource = Instances;
        }
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            List<string> errorMessages = new List<string>();

            // Refresh DataGrid bindings to ensure latest user inputs are captured
            InstancesDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
            InstancesDataGrid.CommitEdit();

            // Count the number of bedrooms to apply the correct rule
            int bedroomCount = Instances.Count(r => r.RoomType == "Bedroom");

            foreach (var instance in Instances)
            {
                double minAreaFt2 = 0.0; // Default: no minimum constraint

                // Define minimum area requirements in ft²
                switch (instance.RoomType)
                {
                    case "Living Room":
                        minAreaFt2 = 118.0;
                        break;
                    case "Dining Room":
                        minAreaFt2 = 75.0;
                        break;
                    case "Kitchen":
                        minAreaFt2 = 45.0;
                        break;
                    case "Washroom":
                        minAreaFt2 = 34.0;
                        break;
                    case "Bedroom":
                        if (bedroomCount == 1)
                            minAreaFt2 = 105.0;
                        else if (bedroomCount == 2)
                            minAreaFt2 = 75.0;
                        else if (bedroomCount > 2)
                            minAreaFt2 = 45.0;
                        break;
                    default:
                        minAreaFt2 = 0.0; // Office, Library, Den, TV Room, Game Room, Storage have no minimum
                        break;
                }

                // Ensure area values are in ft² (if they are mistakenly stored in m²)
                double areaFt2 = instance.Area; // Assume values are already in ft²

                // Validate minimum area
                if (minAreaFt2 > 0 && areaFt2 < minAreaFt2)
                {
                    errorMessages.Add($"- {instance.Name}: {areaFt2:F2} ft² (Minimum required: {minAreaFt2} ft²)");
                }
            }

            // If validation fails, show error message and prevent closing
            if (errorMessages.Count > 0)
            {
                MessageBox.Show($"Error: The following rooms have an area below the minimum required size:\n\n" +
                                string.Join("\n", errorMessages),
                                "Invalid Area", MessageBoxButton.OK, MessageBoxImage.Warning);
                return; // Prevents the dialog from closing
            }

            // If validation passes, allow closing
            this.DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}
