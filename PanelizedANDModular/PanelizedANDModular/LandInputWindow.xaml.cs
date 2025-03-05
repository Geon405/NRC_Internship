using System;
using System.Windows;

namespace PanelizedAndModularFinal
{
    public partial class LandInputWindow : Window
    {
        // Public property to retrieve the computed land area.
        public double LandArea { get; private set; }

        public LandInputWindow()
        {
            InitializeComponent();
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(txtWidth.Text, out double width) &&
                double.TryParse(txtHeight.Text, out double height) &&
                width > 0 && height > 0)
            {
                LandArea = width * height; // Compute area in square feet.
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
