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

namespace PanelizedAndModularFinal
{
    /// <summary>
    /// Interaction logic for RoomInstancesWindow.xaml
    /// This partial class is linked with the XAML file defining the window's UI.
    /// </summary>
    public partial class RoomInstancesWindow : Window
    {
        // This property holds a list of room instance rows; each row represents an instance of a room.
        public List<RoomInstanceRow> Instances { get; set; }

        // Constructor for the RoomInstancesWindow.
        // It accepts a list of RoomInstanceRow objects to be displayed in the DataGrid.
        public RoomInstancesWindow(List<RoomInstanceRow> instances)
        {
            InitializeComponent(); // Initializes the components defined in the XAML file.
            Instances = instances; // Assigns the passed-in list to the Instances property.
            InstancesDataGrid.ItemsSource = Instances; // Binds the Instances list to the DataGrid for display.
        }

        // Event handler for the OK button click.
        // When the OK button is clicked, this sets the DialogResult to true.
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true; // Indicates that the user accepted/confirmed the action.
        }

        // Event handler for the Cancel button click.
        // When the Cancel button is clicked, this sets the DialogResult to false.
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false; // Indicates that the user canceled/declined the action.
        }
    }
}
