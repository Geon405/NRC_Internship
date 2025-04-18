using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PanelizedAndModularFinal;

public class ModuleArrangement1
{
    // Executes the entire module arrangement process with given parameters.
    public void ExecuteArrangement(Document doc, List<ModuleType> moduleTypes, string selectedCombination)
    {
        // Parse the combination into the modules to use.
        List<ModuleType> scenarioModules = ParseSelectedCombination(selectedCombination, moduleTypes);
        int n = scenarioModules.Count;
        int totalPermutations = 1 << n; // 2^n possibilities.

        BoundingBoxXYZ cropBox = doc.ActiveView.CropBox;
        double landOriginX = cropBox.Min.X;
        double landOriginY = cropBox.Min.Y;
        double landWidth = cropBox.Max.X - cropBox.Min.X;
        double landHeight = cropBox.Max.Y - cropBox.Min.Y;

        using (Transaction trans = new Transaction(doc, "Module Arrangement"))
        {
            trans.Start();

            for (int k = 0; k < totalPermutations; k++)
            {
                List<PlacedModule> placed = new List<PlacedModule>();

                for (int i = 0; i < n; i++)
                {
                    bool orientation = ((k >> i) & 1) == 1;
                    double length = orientation ? scenarioModules[i].Width : scenarioModules[i].Length;
                    double width = orientation ? scenarioModules[i].Length : scenarioModules[i].Width;

                    placed.Add(new PlacedModule
                    {
                        X = landOriginX + placed.Sum(pm => pm.Width),
                        Y = landOriginY,
                        Width = width,
                        Height = length
                    });
                }

                if (CheckConstraints(placed) &&
                    CheckWithinLandBoundary(placed, landOriginX, landOriginY, landWidth, landHeight))
                {
                    DrawModulesInRed(doc, placed);
                }
            }

            trans.Commit();
        }
    }

    private List<ModuleType> ParseSelectedCombination(string selectedCombination, List<ModuleType> allTypes)
    {
        return allTypes;
    }

    private bool CheckConstraints(List<PlacedModule> modules)
    {
        return NoOverlap(modules) && HasSharedEdge(modules) && !HasCornerConnection(modules);
    }

    private bool NoOverlap(List<PlacedModule> modules)
    {
        for (int i = 0; i < modules.Count; i++)
        {
            for (int j = i + 1; j < modules.Count; j++)
            {
                var mi = modules[i];
                var mj = modules[j];
                bool separated =
                    mi.X + mi.Width <= mj.X ||
                    mj.X + mj.Width <= mi.X ||
                    mi.Y + mi.Height <= mj.Y ||
                    mj.Y + mj.Height <= mi.Y;
                if (!separated) return false;
            }
        }
        return true;
    }

    private bool HasSharedEdge(List<PlacedModule> modules)
    {
        for (int i = 0; i < modules.Count; i++)
        {
            for (int j = i + 1; j < modules.Count; j++)
            {
                var mi = modules[i];
                var mj = modules[j];
                bool sideBySide =
                    (Math.Abs(mi.X + mi.Width - mj.X) < 1e-6 ||
                     Math.Abs(mj.X + mj.Width - mi.X) < 1e-6) &&
                    !(mi.Y + mi.Height <= mj.Y || mj.Y + mj.Height <= mi.Y);
                bool stacked =
                    (Math.Abs(mi.Y + mi.Height - mj.Y) < 1e-6 ||
                     Math.Abs(mj.Y + mj.Height - mi.Y) < 1e-6) &&
                    !(mi.X + mi.Width <= mj.X || mj.X + mj.Width <= mi.X);
                if (sideBySide || stacked) return true;
            }
        }
        return false;
    }

    private bool HasCornerConnection(List<PlacedModule> modules)
    {
        for (int i = 0; i < modules.Count; i++)
        {
            for (int j = i + 1; j < modules.Count; j++)
            {
                var mi = modules[i];
                var mj = modules[j];
                bool corner =
                    Math.Abs(mi.X + mi.Width - mj.X) < 1e-6 &&
                    Math.Abs(mi.Y - (mj.Y + mj.Height)) < 1e-6;
                if (corner) return true;
            }
        }
        return false;
    }

    private bool CheckWithinLandBoundary(List<PlacedModule> modules, double originX, double originY, double landWidth, double landHeight)
    {
        double maxX = originX + landWidth;
        double maxY = originY + landHeight;
        foreach (var module in modules)
        {
            if (module.X < originX || module.Y < originY ||
                (module.X + module.Width) > maxX ||
                (module.Y + module.Height) > maxY)
                return false;
        }
        return true;
    }

    private void DrawModulesInRed(Document doc, List<PlacedModule> modules)
    {
        foreach (var m in modules)
        {
            XYZ p1 = new XYZ(m.X, m.Y, 0);
            XYZ p2 = new XYZ(m.X + m.Width, m.Y, 0);
            XYZ p3 = new XYZ(m.X + m.Width, m.Y + m.Height, 0);
            XYZ p4 = new XYZ(m.X, m.Y + m.Height, 0);

            var boundary = new List<Curve>
            {
                Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p4),
                Line.CreateBound(p4, p1)
            };

            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            ds.ApplicationId = "ModuleArrangement1";
            ds.ApplicationDataId = Guid.NewGuid().ToString();

            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Color(255, 0, 0));
            doc.ActiveView.SetElementOverrides(ds.Id, ogs);

            Solid moduleSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new CurveLoop[] { CurveLoop.Create(boundary) },
                XYZ.BasisZ, 0.1);

            ds.SetShape(new GeometryObject[] { moduleSolid });
        }
    }

    internal void ExecuteArrangement(Document doc, List<PanelizedAndModularFinal.ModuleType> moduleTypes, string selectedCombination)
    {
        throw new NotImplementedException();
    }
}

public class PlacedModule
{
    public double X;
    public double Y;
    public double Width;
    public double Height;
}

public class ModuleType
{
    public string Name { get; set; }
    public double Length { get; set; }
    public double Width { get; set; }
}
