using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Shapes;
using Autodesk.Revit.UI;

namespace PanelizedAndModularFinal
{
    public partial class LayoutSelectionWindow : Window
    {
        public List<LayoutPreview> LayoutPreviews { get; private set; }
        public List<SpaceNode> SelectedLayout { get; private set; }

        public LayoutSelectionWindow(List<List<SpaceNode>> layoutOptions)
        {
            InitializeComponent();

            LayoutPreviews = new List<LayoutPreview>();
            int index = 1;

            foreach (var layout in layoutOptions)
            {
                LayoutPreview preview = new LayoutPreview
                {
                    Title = $"Layout {index}",
                    Layout = layout,
                    Thumbnail = GenerateThumbnailFromLayout(layout) // No connectivity matrix used yet
                };
                LayoutPreviews.Add(preview);
                index++;
            }

            // Bind to UI
            LayoutItemsControl.ItemsSource = LayoutPreviews;
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is LayoutPreview selectedPreview)
            {
                SelectedLayout = selectedPreview.Layout;

                // Debugging: Check the number of rooms in the selected layout
                TaskDialog.Show("Debug", $"Selected Layout: {SelectedLayout.Count} rooms");

                DialogResult = true;
                Close();
            }
        }


        //private void SelectButton_Click(object sender, RoutedEventArgs e)
        //{
        //    if (sender is Button button && button.Tag is LayoutPreview selectedPreview)
        //    {
        //        SelectedLayout = selectedPreview.Layout;
        //        DialogResult = true;
        //        Close();
        //    }
        //}

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private BitmapSource GenerateThumbnailFromLayout(List<SpaceNode> layout)
        {
            int width = 400, height = 400;
            double padding = 50;  // Increased padding to ensure circles fit

            if (layout == null || layout.Count == 0)
                return null;

            // Find bounding box of layout including circles' radius
            double minX = layout.Min(node => node.Position.X - Math.Sqrt(node.Area / Math.PI));
            double minY = layout.Min(node => node.Position.Y - Math.Sqrt(node.Area / Math.PI));
            double maxX = layout.Max(node => node.Position.X + Math.Sqrt(node.Area / Math.PI));
            double maxY = layout.Max(node => node.Position.Y + Math.Sqrt(node.Area / Math.PI));

            double layoutWidth = maxX - minX;
            double layoutHeight = maxY - minY;

            // Scale to fit preview while maintaining aspect ratio
            double scaleX = (width - 2 * padding) / layoutWidth;
            double scaleY = (height - 2 * padding) / layoutHeight;
            double scale = Math.Min(scaleX, scaleY) * 0.8; // Reduce scale slightly for better fit

            // Offset for centering
            double offsetX = (width - (layoutWidth * scale)) / 2 - (minX * scale);
            double offsetY = (height - (layoutHeight * scale)) / 2 - (minY * scale);

            // Create drawing visual
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

                // Draw rooms as properly scaled circles
                foreach (var node in layout)
                {
                    double x = (node.Position.X * scale) + offsetX;
                    double y = (node.Position.Y * scale) + offsetY;
                    double radius = Math.Sqrt(node.Area / Math.PI) * scale;

                    Brush fillBrush = new SolidColorBrush(node.WpfColor);
                    Pen borderPen = new Pen(Brushes.Black, 1);

                    dc.DrawEllipse(fillBrush, borderPen, new Point(x, y), radius, radius);

                    FormattedText text = new FormattedText(
                        node.Name,
                        System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Arial"),
                        12,
                        Brushes.Black,
                        VisualTreeHelper.GetDpi(visual).PixelsPerDip);

                    dc.DrawText(text, new Point(x - text.Width / 2, y - radius - 10));
                }
            }

            RenderTargetBitmap rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            return rtb;
        }
    }

    public class LayoutPreview
    {
        public string Title { get; set; }
        public List<SpaceNode> Layout { get; set; }
        public BitmapSource Thumbnail { get; set; }
    }
}