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
        public bool UserWentBack { get; private set; } = false;


        public RoomInputWindow()
        {
            InitializeComponent();

            // Predefine the room types with default colors
            RoomTypes = new List<RoomTypeRow>()
{
    new RoomTypeRow { Name = "Bedroom",      Color = System.Windows.Media.Colors.DodgerBlue,      Quantity = 0 },
    new RoomTypeRow { Name = "Library",      Color = System.Windows.Media.Colors.MediumPurple,    Quantity = 0 },
    new RoomTypeRow { Name = "Den",          Color = System.Windows.Media.Colors.ForestGreen,     Quantity = 0 },
    new RoomTypeRow { Name = "Living Room",  Color = System.Windows.Media.Colors.Tomato,          Quantity = 0 },
    new RoomTypeRow { Name = "Dining Room",  Color = System.Windows.Media.Colors.OrangeRed,       Quantity = 0 },
    new RoomTypeRow { Name = "Dining",       Color = System.Windows.Media.Colors.Gold,            Quantity = 0 },
    new RoomTypeRow { Name = "Half-Bathroom",Color = System.Windows.Media.Colors.SlateGray,       Quantity = 0 },
    new RoomTypeRow { Name = "Kitchen",      Color = System.Windows.Media.Colors.Orange,          Quantity = 0 },
    new RoomTypeRow { Name = "Laundry Room", Color = System.Windows.Media.Colors.MediumTurquoise, Quantity = 0 },
    new RoomTypeRow { Name = "Washroom",     Color = System.Windows.Media.Colors.MediumSeaGreen,  Quantity = 0 },
    new RoomTypeRow { Name = "Storage",      Color = System.Windows.Media.Colors.SandyBrown,      Quantity = 0 }
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

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            UserWentBack = true;
            DialogResult = false;
            Close();
        }
    }
}
