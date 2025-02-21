#region Namespaces
using System;
using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Media;

#endregion

namespace PanelizedAndModularFinal
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
       



        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // Step 1: Get Room Inputs from User
                RoomInputWindow firstWindow = new RoomInputWindow();
                bool? firstResult = firstWindow.ShowDialog();
                if (firstResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled at the first window.");
                    return Result.Cancelled;
                }

                List<RoomTypeRow> userSelections = firstWindow.RoomTypes;
                List<RoomInstanceRow> instanceRows = new List<RoomInstanceRow>();

                // Step 2: Generate Room Instances
                foreach (var row in userSelections)
                {
                    if (row.Quantity <= 0) continue;
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

                // Step 3: Open Second Window for Room Adjustments
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
                    
                    //adding every room information in a list called spaces of type SpaceNode
                    var node = new SpaceNode(inst.Name, inst.RoomType, area, position, inst.WpfColor);
                    spaces.Add(node);
                }



                //EXTRA STEP MAKE ADJACENCY MATRIX



                PreferredAdjacencyWindow adjacencyWindow = new PreferredAdjacencyWindow(spaces);
                bool? result = adjacencyWindow.ShowDialog();
                if (result != true)
                {
                    TaskDialog.Show("Canceled", "User canceled at the preferred adjacency matrix window.");
                    return Result.Cancelled;

                }

                //2d matrix value for preffered adjacency matrix
                int[,] preferredAdjacency = adjacencyWindow.PreferredAdjacency;





                // Step 4: Get Connectivity Matrix
                ConnectivityMatrixWindow connectivityWindow = new ConnectivityMatrixWindow(spaces);
                bool? connectivityResult = connectivityWindow.ShowDialog();
                if (connectivityResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled at the connectivity matrix window.");
                    return Result.Cancelled;
                }


                //2d matrix value for connectivity matrix
                int[,] adjacencyMatrix = connectivityWindow.ConnectivityMatrix;

                // Step 5: Open Edge Weights Window
                EdgeWeightsWindow weightsWindow = new EdgeWeightsWindow(spaces, adjacencyMatrix);
                bool? weightResult = weightsWindow.ShowDialog();
                if (weightResult != true)
                {
                    TaskDialog.Show("Canceled", "User canceled the edge weights window.");
                    return Result.Cancelled;
                }

                // Retrieve weighted adjacency matrix This will hold the final user-entered weights.
                double?[,] weightedAdjMatrix = weightsWindow.WeightedAdjacencyMatrix;







                // Get bounding box from Crop Region or fallback to overall bounding box
                View activeView = doc.ActiveView;
                BoundingBoxXYZ viewBox = null;
                if (activeView.CropBoxActive && activeView.CropBox != null)
                    viewBox = activeView.CropBox;
                else
                    viewBox = activeView.get_BoundingBox(null);

                // Apply force-directed layout with bounding-box clamping
                ApplyForceDirectedLayout(spaces, preferredAdjacency, weightedAdjMatrix, viewBox);


                // Now create edges (lines) and circular geometry
                // using the updated node positions in 'spaces'.

















                // Step 6: Create Room Connections with Weights
                using (Transaction tx = new Transaction(doc, "Connect Rooms"))
                {
                    tx.Start();
                    for (int i = 0; i < spaces.Count; i++)
                    {
                        for (int j = i + 1; j < spaces.Count; j++)
                        {
                            if (weightedAdjMatrix[i, j].HasValue)
                            {
                                Line connectionLine = Line.CreateBound(spaces[i].Position, spaces[j].Position);
                                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, spaces[i].Position);
                                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                                ModelCurve curve = doc.Create.NewModelCurve(connectionLine, sketchPlane);
                            }
                        }
                    }
                    tx.Commit();
                }






                // Step 7: Create Circular Rooms
       
                using (Transaction tx = new Transaction(doc, "Create Rooms"))
                {
                    tx.Start();
                    foreach (var space in spaces)
                    {
                        // Pass space.Name and uidoc.ActiveView.Id to the modified method
                        CreateCircleNode(doc, space.Position, space.Area, space.WpfColor, space.Name, uidoc.ActiveView.Id);
                    }
                    tx.Commit();
                }








                TaskDialog.Show("Revit", $"Created {spaces.Count} room(s) with connections.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", ex.Message);
                return Result.Failed;
            }
        }

        // Method to create circular room representation

        private void CreateCircleNode(Document doc, XYZ position, double area, System.Windows.Media.Color wpfColor, string roomName, ElementId viewId)
        {
            double areaFt2 = area * 10.7639;
            double radius = Math.Sqrt(areaFt2 / Math.PI);
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, position);
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

            Curve fullCircle = Ellipse.CreateCurve(position, radius, radius, XYZ.BasisX, XYZ.BasisY, 0, 2 * Math.PI);
            Autodesk.Revit.DB.Color revitColor = new Autodesk.Revit.DB.Color(wpfColor.R, wpfColor.G, wpfColor.B);
            GraphicsStyle gs = GetOrCreateLineStyle(doc, $"RoomStyle_{wpfColor}", revitColor);
            ModelCurve modelCurve = doc.Create.NewModelCurve(fullCircle, sketchPlane);
            modelCurve.LineStyle = gs;

            // Set a thicker line weight using an override
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineWeight(9);  // Change 5 to your desired thickness value
            doc.ActiveView.SetElementOverrides(modelCurve.Id, ogs);

            // Add text note (if needed)
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                                                    .OfClass(typeof(TextNoteType));
            TextNoteType textNoteType = collector.FirstElement() as TextNoteType;
            if (textNoteType != null)
            {
                TextNote.Create(doc, viewId, position, roomName, textNoteType.Id);
            }
        }









        // Method to get or create a line style
        private GraphicsStyle GetOrCreateLineStyle(Document doc, string styleName, Autodesk.Revit.DB.Color revitColor)
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





        // Add force-directed layout parameters (adjust as needed).
        private const int ITERATIONS = 100;
        private const double PREFERRED_ADJ_FACTOR = 2.0;   // Scale factor for preferred adjacency
        private const double SPRING_CONSTANT = 0.01;       // Attraction constant
        private const double REPULSION_CONSTANT = 100.0;   // Repulsion constant
        private const double DAMPING = 0.85;               // Damping factor to stabilize movement


        /// <summary>
        /// Simple force-directed layout respecting preferred adjacency, edge weights, and no overlap.
        /// </summary>
        // MAIN LAYOUT METHOD (modified to clamp to bounding box)
        private void ApplyForceDirectedLayout(List<SpaceNode> spaces,
                                              int[,] preferredAdjMatrix,
                                              double?[,] weightedAdjMatrix,
                                              BoundingBoxXYZ viewBox)
        {
            for (int iter = 0; iter < ITERATIONS; iter++)
            {
                XYZ[] forces = new XYZ[spaces.Count];
                for (int i = 0; i < forces.Length; i++)
                    forces[i] = XYZ.Zero;

                // Calculate pairwise forces
                for (int i = 0; i < spaces.Count; i++)
                {
                    for (int j = i + 1; j < spaces.Count; j++)
                    {
                        XYZ posI = spaces[i].Position;
                        XYZ posJ = spaces[j].Position;
                        XYZ delta = posJ - posI;
                        double distance = delta.GetLength();
                        if (distance < 1e-6) distance = 1e-6;

                        // Repulsion
                        double repForce = REPULSION_CONSTANT / (distance * distance);
                        XYZ repulsion = repForce * delta.Normalize();

                        // Determine weight for attraction:
                        double weight = 0.0;
                        if (weightedAdjMatrix[i, j].HasValue && weightedAdjMatrix[i, j].Value > 0)
                        {
                            weight = weightedAdjMatrix[i, j].Value;
                        }
                        // If preferred adjacency exists but no weighted connection, use default weight
                        if (preferredAdjMatrix[i, j] == 1)
                        {
                            if (weight == 0.0)
                            {
                                weight = 1.0;  // default weight for preferred adjacency
                            }
                            else
                            {
                                weight *= PREFERRED_ADJ_FACTOR;  // amplify if already weighted
                            }
                        }

                        double attrForce = 0.0;
                        if (weight > 0)
                        {
                            attrForce = SPRING_CONSTANT * weight * (distance - 1.0);
                        }
                        XYZ attraction = -attrForce * delta.Normalize();

                        // Net force
                        XYZ forceIJ = repulsion + attraction;
                        forces[i] -= forceIJ;
                        forces[j] += forceIJ;
                    }
                }


                // Update positions with damping
                for (int i = 0; i < spaces.Count; i++)
                {
                    XYZ velocity = forces[i] * DAMPING;
                    double maxDisplacement = 5.0;
                    if (velocity.GetLength() > maxDisplacement)
                        velocity = velocity.Normalize() * maxDisplacement;
                    spaces[i].Position += velocity;
                }

                // Resolve overlap collisions
                ResolveCollisions(spaces);

                // Clamp positions so nodes remain in the Crop Region
                ClampNodesToCropRegion(spaces, viewBox);
            }
        }

        /// <summary>
        /// Pushes nodes apart if their circles overlap.
        /// </summary>
        private void ResolveCollisions(List<SpaceNode> spaces)
        {
            for (int i = 0; i < spaces.Count; i++)
            {
                for (int j = i + 1; j < spaces.Count; j++)
                {
                    XYZ posI = spaces[i].Position;
                    XYZ posJ = spaces[j].Position;
                    double radiusI = GetCircleRadius(spaces[i].Area);
                    double radiusJ = GetCircleRadius(spaces[j].Area);

                    XYZ delta = posJ - posI;
                    double distance = delta.GetLength();
                    double minDist = radiusI + radiusJ;

                    if (distance < minDist)
                    {
                        double overlap = minDist - distance + 0.001;
                        XYZ pushDir = delta.Normalize();
                        // Move each node half the overlap distance away from each other
                        spaces[i].Position -= 0.5 * overlap * pushDir;
                        spaces[j].Position += 0.5 * overlap * pushDir;
                    }
                }
            }
        }

        private double GetCircleRadius(double areaInM2)
        {
            double areaFt2 = areaInM2 * 10.7639;
            return Math.Sqrt(areaFt2 / Math.PI);
        }





        // NEW: Clamp node centers inside the bounding box (with radius considered)
        private void ClampNodesToCropRegion(List<SpaceNode> spaces, BoundingBoxXYZ bb)
        {
            if (bb == null) return; // Fallback if no bounding box

            XYZ min = bb.Min;
            XYZ max = bb.Max;
            foreach (var space in spaces)
            {
                double r = GetCircleRadius(space.Area);
                XYZ pos = space.Position;

                double x = pos.X;
                double y = pos.Y;
                double z = pos.Z;

                // Clamp X
                x = Math.Max(x, min.X + r);
                x = Math.Min(x, max.X - r);

                // Clamp Y
                y = Math.Max(y, min.Y + r);
                y = Math.Min(y, max.Y - r);

                // Z is typically 0 for plan view
                space.Position = new XYZ(x, y, z);
            }
        }






    }


}

