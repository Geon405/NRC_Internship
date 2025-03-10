using System;
using System.Windows;

namespace PanelizedAndModularFinal
{
    public partial class LandInputWindow : Window
    {
        // Public read-only properties for width and height
        public double InputWidth { get; private set; }
        public double InputHeight { get; private set; }

        // Public property to retrieve the computed land area.
        public double LandArea { get; private set; }

        public LandInputWindow() 
        {
            InitializeComponent();
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(txtWidth.Text, out double width) &&
                double.TryParse(txtLength.Text, out double height) &&
                width > 0 && height > 0)
            {
                // Set the properties so they can be accessed elsewhere
                InputWidth = width;
                InputHeight = height;
                LandArea = width * height; // Compute area in square feet

                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please enter valid positive numbers for width and height.",
                                "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
