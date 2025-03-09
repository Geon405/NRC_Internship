using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

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

            // Determine if both a Living Room and a Dining room exist for combined validation
            bool hasLiving = Instances.Any(r => r.RoomType == "Living Room");
            bool hasDining = Instances.Any(r => r.RoomType == "Dining");

            int bedroomCounter = 0;

            foreach (var instance in Instances)
            {
                double minAreaFt2 = 0.0;

                if (instance.RoomType == "Bedroom")
                {
                    if (bedroomCounter == 0)
                    {
                        // Bedroom 1 (Master Bedroom)
                        instance.Name = $"Bedroom {bedroomCounter + 1} (Master Bedroom)";
                        minAreaFt2 = 105.0;
                    }
                    else if (bedroomCounter == 1)
                    {
                        // Bedroom 2 (Second Bedroom)
                        instance.Name = $"Bedroom {bedroomCounter + 1} (Second Bedroom)";
                        minAreaFt2 = 75.0;
                    }
                    else
                    {
                        instance.Name = $"Bedroom {bedroomCounter + 1}";
                        minAreaFt2 = 45.0;
                    }
                    bedroomCounter++;
                }
                else
                {
                    switch (instance.RoomType)
                    {
                        case "Living Room":
                            if (hasLiving && hasDining)
                                minAreaFt2 = 0.0;
                            else
                                minAreaFt2 = 118.0;
                            break;
                        case "Dining":
                            if (hasLiving && hasDining)
                                minAreaFt2 = 0.0;
                            else
                                minAreaFt2 = 75.0;
                            break;
                        case "Dining Room":
                            minAreaFt2 = 35.0;
                            break;
                        case "Kitchen":
                            minAreaFt2 = 45.0;
                            break;
                        case "Washroom":
                            minAreaFt2 = 36.0;
                            break;
                        case "Half-Bathroom":
                            minAreaFt2 = 18.0;
                            break;
                        default:
                            minAreaFt2 = 0.0;
                            break;
                    }
                }

                double areaFt2 = instance.Area; // Assume values are already in ft²

                if (minAreaFt2 > 0 && areaFt2 < minAreaFt2)
                {
                    errorMessages.Add($"- {instance.Name}: {areaFt2:F2} ft² (Minimum required: {minAreaFt2} ft²)");
                }
            }

            // Validate the combined area of Living Room and Dining if both are present
            if (hasLiving && hasDining)
            {
                var livingRoom = Instances.First(r => r.RoomType == "Living Room");
                var dining = Instances.First(r => r.RoomType == "Dining");
                double combinedArea = livingRoom.Area + dining.Area;
                if (combinedArea < 145.0)
                {
                    errorMessages.Add($"- Combined Living Room and Dining: {combinedArea:F2} ft² (Minimum required: 145 ft²)");
                }
            }

            // If any room fails individual validation, show error messages and exit
            if (errorMessages.Count > 0)
            {
                MessageBox.Show($"Error: The following rooms have an area below the minimum required size:\n\n" +
                                string.Join("\n", errorMessages),
                                "Invalid Area", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Sum the total room area from all instances
            // Sum the total room area from all instances
            double totalRoomArea = Instances.Sum(instance => instance.Area);
            GlobalData.TotalRoomArea = totalRoomArea;

            // Calculate the maximum total space size based on the land area.
            double maxBuildingSize = 0.6 * GlobalData.LandArea;
            double maxTotalSpaceSize = maxBuildingSize - (0.15 * maxBuildingSize);

            // Check if the total room area exceeds the maximum allowable space size.
            if (totalRoomArea > maxTotalSpaceSize)
            {
                MessageBox.Show("The sum of room areas exceeds the maximum total allowable space size: "
                                + maxTotalSpaceSize + " ft²",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Check if the total room area is less than the maximum total space size.
            if (totalRoomArea < maxTotalSpaceSize)
            {
                double missingArea = maxTotalSpaceSize - totalRoomArea;
                MessageBoxResult result = MessageBox.Show(
                    $"The current total room area is {totalRoomArea:F2} ft², leaving {missingArea:F2} ft² unallocated.\n\n" +
                    "Do you want to proceed with the current setup?\n" +
                    "Click 'Yes' to proceed or 'No' to return to space allocation and add more space.",
                    "Incomplete Allocation", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    // Return to space allocation (the window remains open for further modifications).
                    return;
                }
            }

            // If all validations pass or the user decides to proceed, close the window.
            this.DialogResult = true;
            Close();

        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}
