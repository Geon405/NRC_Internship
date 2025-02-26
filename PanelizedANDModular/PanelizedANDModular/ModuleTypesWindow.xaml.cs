using System;
using System.Collections.Generic;
using System.Windows;

namespace PanelizedAndModularFinal
{
    public partial class ModuleTypesWindow : Window
    {
        public ModuleTypesWindow(double minWidth, double maxHeight)
        {
            InitializeComponent();

            // Calculate the maximum multiplier (n)
            int n = (int)Math.Floor(maxHeight / minWidth);
            if (n < 2)
            {
                MessageBox.Show("Maximum height must be at least twice the minimum width.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
                return;
            }
            List<string> moduleTypes = new List<string>();
            // For each i from 2 to n, create a module type.
            // Example for minWidth=15: Module_Type 1: 15*30 = 450, etc.
            for (int i = 2; i <= n; i++)
            {
                double moduleLength = minWidth * i;
                double area = minWidth * moduleLength;
                moduleTypes.Add($"Module_Type {i - 1}: {minWidth}*{moduleLength} = {area}");
            }
            lbModuleTypes.ItemsSource = moduleTypes;
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
