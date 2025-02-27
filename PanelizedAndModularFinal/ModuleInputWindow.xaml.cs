using System;
using System.Windows;

namespace PanelizedAndModularFinal
{
    public partial class ModuleInputWindow : Window
    {
        // Properties to store the user inputs.
        public double MinWidth { get; set; }
        public double MaxHeight { get; set; }

        public ModuleInputWindow()
        {
            InitializeComponent();
        }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(txtMinWidth.Text, out double minWidth) ||
                !double.TryParse(txtMaxHeight.Text, out double maxHeight))
            {
                MessageBox.Show("Please enter valid numeric values.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (minWidth <= 0 || maxHeight <= 0 || maxHeight < minWidth * 2)
            {
                MessageBox.Show("Invalid dimensions. Max height must be at least twice the min width.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Save the input values in the properties.
            this.MinWidth = minWidth;
            this.MaxHeight = maxHeight;

            // Open the ModuleTypesWindow using the new constructor that accepts minWidth and maxHeight.
            ModuleTypesWindow typesWindow = new ModuleTypesWindow(minWidth, maxHeight);
            typesWindow.ShowDialog();


         

            DialogResult = true;
            Close();

        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
