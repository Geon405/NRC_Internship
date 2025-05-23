using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PanelizedAndModularFinal
{
    public partial class CandidateGraphsWindow : Window
    {
        class GraphModel
        {
            public string Title { get; set; }
            public List<SpaceNode> Spaces { get; set; }
            public int[,] Connectivity { get; set; }
            public double MaxWorldSize { get; set; }
        }

        public CandidateGraphsWindow(
            List<List<SpaceNode>> layouts,
            List<int[,]> connectivities)
        {
            InitializeComponent();

            // 1) build one GraphModel per layout
            var models = new List<GraphModel>();
            for (int i = 0; i < layouts.Count; i++)
            {
                models.Add(new GraphModel
                {
                    Title = $"Graph {i + 1}",
                    Spaces = layouts[i],
                    Connectivity = connectivities[i]
                });
            }

            // 2) compute *one* global max‐span so every canvas scales identically
            double globalMaxSpan = models
              .Select(m =>
              {
                  // world‐bounds including circle radii
                  double minX = m.Spaces.Min(s => s.Position.X - Math.Sqrt(s.Area / Math.PI));
                  double maxX = m.Spaces.Max(s => s.Position.X + Math.Sqrt(s.Area / Math.PI));
                  double minY = m.Spaces.Min(s => s.Position.Y - Math.Sqrt(s.Area / Math.PI));
                  double maxY = m.Spaces.Max(s => s.Position.Y + Math.Sqrt(s.Area / Math.PI));
                  return Math.Max(maxX - minX, maxY - minY);
              })
              .Max();

            // 3) write it back into each model
            foreach (var m in models)
                m.MaxWorldSize = globalMaxSpan;

            GraphsItemsControl.ItemsSource = models;
        }

        private void Canvas_Loaded(object sender, RoutedEventArgs e)
        {
            var canvas = (Canvas)sender;
            var model = (GraphModel)canvas.DataContext;

            // clear any old content
            canvas.Children.Clear();

            double pad = 10.0;
            double world = model.MaxWorldSize;
            double scale = (canvas.ActualWidth - 2 * pad) / world;

            // compute each space's world‐min to shift everything
            double worldMinX = model.Spaces.Min(s => s.Position.X - Math.Sqrt(s.Area / Math.PI));
            double worldMinY = model.Spaces.Min(s => s.Position.Y - Math.Sqrt(s.Area / Math.PI));

            // --- draw edges ---
            int n = model.Spaces.Count;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (model.Connectivity[i, j] == 1)
                    {
                        var A = model.Spaces[i];
                        var B = model.Spaces[j];

                        double ax = pad + (A.Position.X - worldMinX) * scale;
                        double ay = pad + (world - (A.Position.Y - worldMinY)) * scale;
                        double bx = pad + (B.Position.X - worldMinX) * scale;
                        double by = pad + (world - (B.Position.Y - worldMinY)) * scale;

                        var line = new Line
                        {
                            X1 = ax,
                            Y1 = ay,
                            X2 = bx,
                            Y2 = by,
                            Stroke = Brushes.Black,
                            StrokeThickness = 1
                        };
                        canvas.Children.Add(line);
                    }

            // --- draw nodes ---
            foreach (var s in model.Spaces)
            {
                double cx = pad + (s.Position.X - worldMinX) * scale;
                double cy = pad + (world - (s.Position.Y - worldMinY)) * scale;
                double r = Math.Sqrt(s.Area / Math.PI) * scale;

                // circle
                var circle = new Ellipse
                {
                    Width = 2 * r,
                    Height = 2 * r,
                    Fill = new SolidColorBrush(Color.FromArgb(200, s.WpfColor.R, s.WpfColor.G, s.WpfColor.B)),
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(circle, cx - r);
                Canvas.SetTop(circle, cy - r);
                canvas.Children.Add(circle);

                // label
                var lbl = new TextBlock
                {
                    Text = s.Name,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                };
                lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(lbl, cx - lbl.DesiredSize.Width / 2.0);
                Canvas.SetTop(lbl, cy - lbl.DesiredSize.Height / 2.0);
                canvas.Children.Add(lbl);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
    }
}