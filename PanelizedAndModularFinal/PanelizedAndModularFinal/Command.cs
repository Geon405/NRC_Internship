#region Namespaces
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Windows.Media; // For WPF color
#endregion

namespace PanelizedAndModularFinal
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
                              ref string message,
                              ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                RoomInputWindow firstWindow = new RoomInputWindow();
                bool? firstResult = firstWindow.ShowDialog();
                if (firstResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled at the first window.");
                    return Result.Cancelled;
                }

                List<RoomTypeRow> userSelections = firstWindow.RoomTypes;
                List<RoomInstanceRow> instanceRows = new List<RoomInstanceRow>();
                foreach (var row in userSelections)
                {
                    if (row.Quantity <= 0)
                        continue;

                    for (int i = 0; i < row.Quantity; i++)
                    {
                        string instanceName = $"{row.Name} {i + 1}";
                        var instance = new RoomInstanceRow
                        {
                            RoomType = row.Name,
                            Name = instanceName,
                            WpfColor = row.Color,
                            Area = 20.0
                        };
                        instanceRows.Add(instance);
                    }
                }

                if (instanceRows.Count == 0)
                {
                    TaskDialog.Show("Info", "No rooms were requested.");
                    return Result.Cancelled;
                }

                RoomInstancesWindow secondWindow = new RoomInstancesWindow(instanceRows);
                bool? secondResult = secondWindow.ShowDialog();
                if (secondResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled at the second window.");
                    return Result.Cancelled;
                }

                List<SpaceNode> spaces = new List<SpaceNode>();
                Random random = new Random();

                foreach (var inst in secondWindow.Instances)
                {
                    double area = inst.Area < 10.0 ? 10.0 : inst.Area;
                    XYZ position = new XYZ(random.NextDouble() * 100, random.NextDouble() * 100, 0);
                    var node = new SpaceNode(inst.Name, inst.RoomType, area, position, inst.WpfColor);
                    spaces.Add(node);
                }

                using (Transaction tx = new Transaction(doc, "Create Rooms"))
                {
                    tx.Start();
                    foreach (var space in spaces)
                    {
                        CreateCircleNode(doc, space.Position, space.Area, space.WpfColor);
                    }
                    tx.Commit();
                }

                TaskDialog.Show("Revit", $"Created {spaces.Count} room(s).");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", ex.Message);
                return Result.Failed;
            }
        }

        private class SpaceNode
        {
            public string Name { get; set; }
            public string Function { get; set; }
            public double Area { get; set; }
            public XYZ Position { get; set; }
            public System.Windows.Media.Color WpfColor { get; set; }

            public SpaceNode(string name, string function, double area, XYZ position, System.Windows.Media.Color wpfColor)
            {
                Name = name;
                Function = function;
                Area = area;
                Position = position;
                WpfColor = wpfColor;
            }
        }

        private void CreateCircleNode(Document doc, XYZ position, double area, System.Windows.Media.Color wpfColor)
        {
            double areaFt2 = area * 10.7639;
            double radius = Math.Sqrt(areaFt2 / Math.PI);
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, position);
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

            XYZ right = position + new XYZ(radius, 0, 0);
            XYZ up = position + new XYZ(0, radius, 0);
            XYZ left = position + new XYZ(-radius, 0, 0);
            XYZ down = position - (up - position);

            Arc arc1 = Arc.Create(right, left, up);
            Arc arc2 = Arc.Create(left, right, down);

            Autodesk.Revit.DB.Color revitColor = new Autodesk.Revit.DB.Color(wpfColor.R, wpfColor.G, wpfColor.B);
            GraphicsStyle gs = GetOrCreateLineStyle(doc, $"RoomStyle_{wpfColor}", revitColor);

            ModelCurve mc1 = doc.Create.NewModelCurve(arc1, sketchPlane);
            ModelCurve mc2 = doc.Create.NewModelCurve(arc2, sketchPlane);

            mc1.LineStyle = gs;
            mc2.LineStyle = gs;
        }

        GraphicsStyle GetOrCreateLineStyle(Document doc, string styleName, Autodesk.Revit.DB.Color revitColor)
        {
            Category linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            Category subCat = null;
            foreach (Category c in linesCat.SubCategories)
            {
                if (c.Name == styleName)
                {
                    subCat = c;
                    break;
                }
            }

            if (subCat == null)
            {
                subCat = doc.Settings.Categories.NewSubcategory(linesCat, styleName);
            }

            using (SubTransaction st = new SubTransaction(doc))
            {
                st.Start();
                subCat.LineColor = revitColor;
                st.Commit();
            }

            return subCat.GetGraphicsStyle(GraphicsStyleType.Projection);
        }
    }
}
