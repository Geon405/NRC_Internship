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
    /// </summary>
    public partial class RoomInstancesWindow : Window
    {
        // This list holds one row per instance of each room
        public List<RoomInstanceRow> Instances { get; set; }

        public RoomInstancesWindow(List<RoomInstanceRow> instances)
        {
            InitializeComponent();
            Instances = instances;
            InstancesDataGrid.ItemsSource = Instances;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true; // user clicked OK
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false; // user canceled
        }
    }
}
