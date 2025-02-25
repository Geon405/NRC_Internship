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
            // Dictionary containing minimum area (ft²) for each space type
            Dictionary<string, double> minAreaRequirements = new Dictionary<string, double>()
    {
        { "Living combined with dining", 145 },
        { "Only Living", 118 },
        { "Dining", 75 },
        { "Dining room (as part of kitchen)", 35 },
        { "Master Bedroom", 105 },
        { "Second bedroom", 75 },
        { "Bedroom spaces in combination (if there is more than 2 bedrooms)", 45 },
        { "Kitchen", 45 },
        { "Bathroom & water closet room", 34 }
    };

            List<string> errorMessages = new List<string>();

            // Validate each room's area
            foreach (var instance in Instances)
            {
                if (minAreaRequirements.ContainsKey(instance.RoomType))
                {
                    double minRequired = minAreaRequirements[instance.RoomType]; // Get min required area
                    if (instance.Area < minRequired)
                    {
                        errorMessages.Add($"- {instance.Name}: {instance.Area} ft² (Minimum required: {minRequired} ft²)");
                    }
                }
            }

            // If errors exist, show all at once
            if (errorMessages.Count > 0)
            {
                MessageBox.Show($"Error: The following rooms have an area below the minimum required size:\n\n" +
                                string.Join("\n", errorMessages),
                                "Invalid Area", MessageBoxButton.OK, MessageBoxImage.Warning);
                return; // Prevent closing
            }

            this.DialogResult = true; // Proceed if validation passes
        }


        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}