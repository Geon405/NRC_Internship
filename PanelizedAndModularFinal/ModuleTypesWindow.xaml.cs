using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PanelizedAndModularFinal
{
    public partial class ModuleTypesWindow : Window
    {
        // Public property to store module types for access in other classes.
        public List<string> ModuleTypeList { get; private set; }


       
        // Change the property type to hold ModuleType objects.
        public List<ModuleType> ModuleTypes { get; private set; }

        public ModuleTypesWindow(double minWidth, double maxHeight)
        {
            InitializeComponent();

            int n = (int)Math.Floor(maxHeight / minWidth);
            if (n < 1)
            {
                MessageBox.Show("Maximum height must be at least equal to the minimum width.",
                                "Input Error", MessageBoxButton.OK, MessageBoxImage.Error);




                this.Close();
                return;
            }

            List<ModuleType> moduleTypeList = new List<ModuleType>();
            for (int i = 1; i <= n; i++)
            {
                double moduleLength = minWidth * i;
                double area = minWidth * moduleLength;
                moduleTypeList.Add(new ModuleType
                {
                    ID = i,
                    Width = minWidth,
                    Length = moduleLength,
                    Area = area
                });
            }

            ModuleTypes = moduleTypeList;
            // Display using the custom string: "X*i*X" where X is minWidth.
            lbModuleTypes.ItemsSource = moduleTypeList
    .Select((mt, index) =>
        $"Module_Type {index + 1}: X*{index + 1}X = {mt.Width}*{mt.Width * (index + 1)} = {mt.Width * mt.Width * (index + 1)}");

        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;  // Indicate successful completion.
            Close();
        }
    }
}
