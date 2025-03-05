using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace PanelizedAndModularFinal
{
    /// <summary>
    /// Interaction logic for LayoutSelectionWindow.xaml
    /// </summary>
    public partial class LayoutSelectionWindow : Window
    {
        // The list of layout previews to display.
        public ObservableCollection<LayoutPreview> LayoutPreviews { get; set; }

        // The layout the user has selected.
        public LayoutPreview SelectedLayout { get; set; }

        public LayoutSelectionWindow(ObservableCollection<LayoutPreview> previews)
        {
            InitializeComponent();
            LayoutPreviews = previews;
            this.DataContext = this;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedLayout == null)
            {
                MessageBox.Show("Please select a layout.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }

    // A simple class to hold a layout preview.
    public class LayoutPreview
    {
        // A title or name for the layout.
        public string Title { get; set; }
        // The underlying layout data (a list of SpaceNodes).
        public System.Collections.Generic.List<SpaceNode> Layout { get; set; }
        // A thumbnail image to show as preview (could be generated from a Canvas, etc.)
        public ImageSource Thumbnail { get; set; }
    }
}
