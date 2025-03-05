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
    /// Interaction logic for RoomInputWindow.xaml
    /// </summary>
    public partial class RoomInputWindow : Window
    {
        public List<RoomTypeRow> RoomTypes { get; set; }

        public RoomInputWindow()
        {
            InitializeComponent();

            // Predefine the room types with default colors
            RoomTypes = new List<RoomTypeRow>()
            {
                new RoomTypeRow { Name = "Bedroom",      Color = System.Windows.Media.Colors.LightBlue,      Quantity = 0 },
                new RoomTypeRow { Name = "Library",      Color = System.Windows.Media.Colors.Wheat,          Quantity = 0 },
                new RoomTypeRow { Name = "Den",          Color = System.Windows.Media.Colors.Lavender,       Quantity = 0 },
                new RoomTypeRow { Name = "Living Room",  Color = System.Windows.Media.Colors.LightCoral,     Quantity = 0 },
                new RoomTypeRow { Name = "Dining Room",  Color = System.Windows.Media.Colors.Plum,           Quantity = 0 },
                new RoomTypeRow { Name = "Dining",       Color = System.Windows.Media.Colors.Gold,           Quantity = 0 },
                new RoomTypeRow { Name = "Half-Bathroom",Color = System.Windows.Media.Colors.LightGray,      Quantity = 0 },
                new RoomTypeRow { Name = "Kitchen",      Color = System.Windows.Media.Colors.Orange,         Quantity = 0 },
                new RoomTypeRow { Name = "Laundry Room", Color = System.Windows.Media.Colors.SkyBlue,        Quantity = 0 },
                new RoomTypeRow { Name = "Washroom",     Color = System.Windows.Media.Colors.MediumAquamarine, Quantity = 0 },
                new RoomTypeRow { Name = "Storage",      Color = System.Windows.Media.Colors.BurlyWood,      Quantity = 0 }
            };

            RoomsDataGrid.ItemsSource = RoomTypes;
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
