using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PanelizedAndModularFinal
{
    public partial class ChosenLayoutPreviewWindow : Window
    {
        private readonly List<SpaceNode> _spaces;

        public ChosenLayoutPreviewWindow(List<SpaceNode> spaces)
        {
            InitializeComponent();
            _spaces = spaces ?? throw new ArgumentNullException(nameof(spaces));
            Loaded += (s, e) => DrawSpaces();
        }

        private void DrawSpaces()
        {
            const double padding = 20.0;

            // 1) world bounds *including* each circle's extent
            double minX = _spaces.Min(s => s.Position.X - Math.Sqrt(s.Area / Math.PI));
            double maxX = _spaces.Max(s => s.Position.X + Math.Sqrt(s.Area / Math.PI));
            double minY = _spaces.Min(s => s.Position.Y - Math.Sqrt(s.Area / Math.PI));
            double maxY = _spaces.Max(s => s.Position.Y + Math.Sqrt(s.Area / Math.PI));

            double worldW = maxX - minX;
            double worldH = maxY - minY;

            // 2) turn that rectangle into a square
            double worldSize = Math.Max(worldW, worldH);

            // 3) our canvas is a fixed 400×400
            double canvasSize = LayoutCanvas.Width;  // == LayoutCanvas.Height

            // 4) uniform scale so the *entire* square (including radii) fits
            double scale = worldSize > 0
                ? (canvasSize - 2 * padding) / worldSize
                : 1.0;

            // 5) center that square in the canvas
            double squarePx = worldSize * scale;
            double offset = (canvasSize - squarePx) / 2;

            // (optional) draw the bounding square so you can see your actual layout zone
            var border = new Rectangle
            {
                Width = squarePx,
                Height = squarePx,
                Stroke = Brushes.LightGray,
                StrokeThickness = 1
            };
            Canvas.SetLeft(border, offset);
            Canvas.SetTop(border, offset);
            LayoutCanvas.Children.Add(border);

            // 6) draw each circle
            foreach (var space in _spaces)
            {
                // map from world coord → [0..worldSize]
                double localX = (space.Position.X - minX);
                double localY = (space.Position.Y - minY);

                // flip Y so positive Y goes *up*
                double px = offset + localX * scale;
                double py = offset + (worldSize - localY) * scale;

                // diameter in pixels
                double rWorld = Math.Sqrt(space.Area / Math.PI);
                double dPx = 2 * rWorld * scale;

                var circle = new Ellipse
                {
                    Width = dPx,
                    Height = dPx,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    Fill = new SolidColorBrush(Color.FromArgb(
                              200, space.WpfColor.R, space.WpfColor.G, space.WpfColor.B))
                };
                Canvas.SetLeft(circle, px - dPx / 2);
                Canvas.SetTop(circle, py - dPx / 2);
                LayoutCanvas.Children.Add(circle);

                var lbl = new TextBlock
                {
                    Text = space.Name,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                };
                lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(lbl, px - lbl.DesiredSize.Width / 2);
                Canvas.SetTop(lbl, py - lbl.DesiredSize.Height / 2);
                LayoutCanvas.Children.Add(lbl);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
