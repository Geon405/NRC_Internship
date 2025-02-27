using System;
using System.Collections.Generic;
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
            if (n < 2)
            {
                MessageBox.Show("Maximum height must be at least twice the minimum width.",
                                "Input Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
                return;
            }

            List<ModuleType> moduleTypeList = new List<ModuleType>();
            for (int i = 2; i <= n; i++)
            {
                double moduleLength = minWidth * i;
                double area = minWidth * moduleLength;
                moduleTypeList.Add(new ModuleType
                {
                    ID = i - 1,
                    Width = minWidth,
                    Length = moduleLength,
                    Area = area
                });
            }

            // Assign the list of ModuleType objects directly.
            ModuleTypes = moduleTypeList;
            // lbModuleTypes.Items.Clear();
            // Optionally, if you want to display strings in the ListBox:
            lbModuleTypes.ItemsSource = moduleTypeList.Select(mt => mt.ToString());
        }


        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;  // Indicate successful completion.
            Close();
        }
    }

}


