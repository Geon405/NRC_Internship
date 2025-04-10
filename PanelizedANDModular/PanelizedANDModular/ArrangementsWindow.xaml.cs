using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using static PanelizedAndModularFinal.ModuleArrangement;

namespace PanelizedAndModularFinal
{
    public partial class ArrangementsWindow : Window
    {
        // Holds the valid arrangements, each with ModuleRectangles and GridCellIds, etc.
        public List<Arrangement> ValidArrangements { get; private set; }

        // The user's chosen list of module rectangles (the corners for each module).
        public List<XYZ[]> SelectedArrangement { get; private set; }

        private Document _doc;

        public ArrangementsWindow(Document doc, List<ModuleType> moduleTypes, string selectedCombination, int desiredArrangementCount)
        {
            InitializeComponent();
            _doc = doc;

            // Generate valid arrangements.
            ModuleArrangement moduleArrangement = new ModuleArrangement();
            ValidArrangements = moduleArrangement.CreateMultipleSquareLikeArrangements(doc, selectedCombination, moduleTypes, desiredArrangementCount);

            // Build a display list for the ListBox.
            var displayList = new List<string>();
            int index = 1;
            foreach (var arrangement in ValidArrangements)
            {
                double combinedArea = 0.0;
                foreach (var rect in arrangement.ModuleRectangles)
                {
                    double rectMinX = Math.Min(rect[0].X, rect[2].X);
                    double rectMinY = Math.Min(rect[0].Y, rect[2].Y);
                    double rectMaxX = Math.Max(rect[0].X, rect[2].X);
                    double rectMaxY = Math.Max(rect[0].Y, rect[2].Y);
                    double width = rectMaxX - rectMinX;
                    double height = rectMaxY - rectMinY;
                    double moduleArea = width * height;
                    combinedArea += moduleArea;
                }

                displayList.Add(
                    $"Arrangement {index}: Total Combined Area = {combinedArea:F2} sq.units, " +
                    $"Modules = {arrangement.ModuleRectangles.Count}");
                index++;
            }

            lbArrangements.ItemsSource = displayList;
            // No drawing occurs here; drawing is deferred until the OK button is clicked.
        }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            if (lbArrangements.SelectedIndex < 0)
            {
                MessageBox.Show("Please select an arrangement.",
                                "Selection Required",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            // The user chose an arrangement from ValidArrangements.
            Arrangement chosen = ValidArrangements[lbArrangements.SelectedIndex];

            // Store the module rectangles that will be displayed.
            SelectedArrangement = chosen.ModuleRectangles;

            // Now, display the selected arrangement in red.
            DisplaySelectedArrangement();

            // Indicate that the selection is complete.
            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Draws the selected arrangement as red detail curves in the active view.
        /// </summary>
        public void DisplaySelectedArrangement()
        {
            // Create override settings with red color.
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(255, 0, 0)); // Red
            ogs.SetCutLineColor(new Autodesk.Revit.DB.Color(255, 0, 0));

            // Get the active view.
            View activeView = _doc.ActiveView;

            using (Transaction trans = new Transaction(_doc, "Display Selected Arrangement"))
            {
                trans.Start();
                double tol = _doc.Application.ShortCurveTolerance;

                // For each module rectangle in the selected arrangement, draw its boundary.
                foreach (XYZ[] rect in SelectedArrangement)
                {
                    // Connect corners: index 0->1, 1->2, 2->3, then 3->0.
                    for (int i = 0; i < 4; i++)
                    {
                        int next = (i + 1) % 4;
                        Line edge = Line.CreateBound(rect[i], rect[next]);
                        if (edge.Length > tol)
                        {
                            DetailCurve dc = _doc.Create.NewDetailCurve(activeView, edge);
                            activeView.SetElementOverrides(dc.Id, ogs);
                        }
                    }
                }
                trans.Commit();
            }
        }
    }
}
