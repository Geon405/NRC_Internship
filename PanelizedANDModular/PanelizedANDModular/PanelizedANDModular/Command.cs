#region Namespaces
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Microsoft.VisualBasic;

#endregion

namespace PanelizedANDModular
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        private const double MinArea = 10.0;

        public Result Execute(
        ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            try
            {
                // Ask the user for the number of spaces (rooms) they want to create.
                int numberOfRooms = GetNumberOfSpaces();
                // Check if the user input is valid (a positive number).
                if (numberOfRooms <= 0)
                {
                    TaskDialog.Show("Error", "Invalid number of rooms.");
                    return Result.Failed;
                }

                // Get a list of user-defined spaces with properties like name, function, area, and position.
                // SpaceNode is a class we created with attributes such as name, fct, area and position
                List<SpaceNode> spaces = GetUserDefinedSpaces(numberOfRooms);

                // For each space defined by the user, create a visual representation (a circle) in the document.
                foreach (var space in spaces)
                {
                    TaskDialog.Show("Space Info",
                        $"Name: {space.Name}\nFunction: {space.Function}\nArea: {space.Area:F2} m²");
                }

                // Define connectivity between each pair of spaces (user inputs adjacency, weights, etc.)
                List<Edge> edges = DefineConnectivityBetweenSpaces(spaces);

                // Apply force-directed layout to adjust positions so circles don't overlap
                // and connected circles tend to be tangent.
                PerformForceDirectedLayout(spaces, edges,
                    iterations: 500,   // Increase if needed
                    kAttract: 0.05,
                    kRepulse: 0.05,
                    damping: 0.5);

                // Start a new transaction because we are going to modify the Revit document.
                using (Transaction trans = new Transaction(doc, "Visualize Circle Spaces"))
                {
                    trans.Start();

                    // Create circles for each space
                    foreach (var space in spaces)
                    {
                        CreateCircleNode(doc, space.Position, space.Area);
                    }

                    // Optionally, create a visual representation of these connections as lines.
                    foreach (var edge in edges)
                    {
                        CreateEdgeLine(doc, edge.NodeA.Position, edge.NodeB.Position);
                    }

                    // Commit the transaction to apply all changes to the document.
                    trans.Commit();
                }

                // Show a final message confirming that the spaces have been added.
                TaskDialog.Show("Revit", $"{numberOfRooms} Rooms have been added as circle-nodes.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                // If any error occurs, show an error message and return a failed result.
                message = ex.Message;
                TaskDialog.Show("Error", $"An error occurred: {ex.Message}");
                return Result.Failed;
            }
        }

        private int GetNumberOfSpaces()
        {
            // Display an input box for the user asking for the number of spaces.
            string input = Interaction.InputBox("Enter the number of spaces:", "Number of Spaces", "5");

            if (int.TryParse(input, out int numberOfSpaces))
                return numberOfSpaces;
            // Return -1 if invalid input
            return -1;
        }

        private class SpaceNode
        {
            public string Name { get; set; }       // The name of the space.
            public string Function { get; set; }   // The type or function of the space (e.g., Bedroom).
            public double Area { get; set; }       // The area of the space.
            public XYZ Position { get; set; }      // The position of the space in 3D space.

            // Constructor 
            public SpaceNode(string name, string function, double area, XYZ position)
            {
                Name = name;
                Function = function;
                Area = area;
                Position = position;
            }
        }

        // Define a class to represent an edge (connection) between two spaces.
        private class Edge
        {
            public SpaceNode NodeA { get; }            // First space node.
            public SpaceNode NodeB { get; }            // Second space node.
            public double Weight { get; }              // A value representing the connection strength.
            public bool PreferredAdjacency { get; }    // Whether the user prefers these spaces to be adjacent.

            // Constructor to initialize an edge with two space nodes, a weight, and adjacency preference.
            public Edge(SpaceNode a, SpaceNode b, double weight, bool preferredAdjacency)
            {
                NodeA = a;
                NodeB = b;
                Weight = weight;
                PreferredAdjacency = preferredAdjacency;
            }
        }

        private List<SpaceNode> GetUserDefinedSpaces(int numberOfSpaces)
        {
            // Create an empty list that will store all the spaces (rooms) defined by the user.
            List<SpaceNode> spaces = new List<SpaceNode>();

            // Create a Random object to generate random positions.
            Random random = new Random();

            // Define a conversion factor to change area from square meters (m²) to square feet (ft²).
            // This is useful if your calculations require feet (for example, when using Revit units).
            double areaConversion = 10.7639;

            // Set a limit on how many times we try to find a non-overlapping position for each space.
            int maxAttempts = 100;

            // Loop through the number of spaces the user wants to create.
            for (int i = 0; i < numberOfSpaces; i++)
            {
                // Prompt the user to input the function/type of the space (e.g., "Bedroom" or "Kitchen").
                string function = Interaction.InputBox(
                    $"Enter the function for Space {i + 1} (e.g., Bedroom, Kitchen):",
                    "Space Function", "Bedroom");

                // Prompt the user to input a name for the space.
                string name = Interaction.InputBox(
                    $"Enter the name for {function} {i + 1}:",
                    "Space Name", $"{function}");
                // If the user leaves the name empty or only spaces, default the name to the function.
                if (string.IsNullOrWhiteSpace(name))
                    name = function;

                // Prompt the user to enter the area for the space in square meters.
                string areaInput = Interaction.InputBox(
                    $"Enter the area (m²) for {name}:",
                    "Space Area", "20");

                // Convert the string input into a double (a number with decimals).
                double area;
                if (!double.TryParse(areaInput, out area) || area <= 0)
                {
                    // If the input is invalid (cannot be converted or is 0 or negative), show an error message.
                    TaskDialog.Show("Error", "Invalid area input. Setting area to 10 m².");
                    area = 10; // Default to 10 m².
                }

                // Ensure the area meets the minimum allowed area. (MinArea is defined elsewhere.)
                if (area < MinArea)
                {
                    // If the area is too small, notify the user and set it to the minimum.
                    TaskDialog.Show("Info",
                        $"Area below minimum ({MinArea} m²). Setting {name}'s area to {MinArea} m².");
                    area = MinArea;
                }

                // Calculate the radius of the circle that represents this space.
                // First, convert the area from square meters to square feet.
                double areaInSquareFeet = area * areaConversion;

                double radius = Math.Sqrt(areaInSquareFeet / Math.PI);

                // Finds a random position for the space that does not overlap with others.
                XYZ position = null;           // Start with no position.
                bool positionFound = false;
                int attempts = 0;

                // Try to find a non-overlapping position until a valid one is found or maxAttempts is reached.
                while (!positionFound && attempts < maxAttempts)
                {
                    // Generate a candidate position with random x and y values (between 0 and 100) and z set to 0.
                    XYZ candidate = new XYZ(random.NextDouble() * 100, random.NextDouble() * 100, 0);
                    bool overlaps = false; // Assume this candidate does not overlap any existing space.

                    // Check for each variable in the list of object of spaces
                    foreach (var existingSpace in spaces)
                    {
                        // For each existing space, calculate its circle's radius.
                        double existingAreaInSquareFeet = existingSpace.Area * areaConversion;
                        double existingRadius = Math.Sqrt(existingAreaInSquareFeet / Math.PI);

                        // Calculate the distance between the candidate's position and the existing space's position.
                        // If this distance is less than the sum of the two radii, the circles would overlap.
                        if (candidate.DistanceTo(existingSpace.Position) < (radius + existingRadius))
                        {
                            overlaps = true;
                            break;
                        }
                    }

                    // If the candidate does not overlap any existing circles, use it as the position.
                    if (!overlaps)
                    {
                        position = candidate;
                        positionFound = true;
                    }

                    attempts++;
                }

                // If no valid position is found after maxAttempts, warn the user and choose a random position.
                if (!positionFound)
                {
                    TaskDialog.Show("Warning", $"Could not find non-overlapping position for {name} after {maxAttempts} attempts. Using a random position.");
                    position = new XYZ(random.NextDouble() * 100, random.NextDouble() * 100, 0);
                }

                // Create a new SpaceNode object with the name, function, area, and determined position.
                // Then, add this new space to our list of spaces.
                spaces.Add(new SpaceNode(name, function, area, position));
            }

            // After all spaces have been defined, return the list.
            return spaces;
        }

        // This method creates a circle (using arcs) to represent a space in the Revit document.
        private void CreateCircleNode(Document doc, XYZ position, double area)
        {
            // Convert area from square meters to square feet since Revit uses feet.
            double areaInSquareFeet = area * 10.7639;
            // Calculate the radius of a circle based on its area.
            double radius = Math.Sqrt(areaInSquareFeet / Math.PI);

            // Create a plane with the normal vector pointing up (Z-axis) at the given position.
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, position);
            // Create a sketch plane on which the circle will be drawn.
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

            // Calculate four key points around the circle (right, up, left, down).
            XYZ right = position + new XYZ(radius, 0, 0);
            XYZ up = position + new XYZ(0, radius, 0);
            XYZ left = position + new XYZ(-radius, 0, 0);
            // 'down' is calculated as the opposite of 'up' from the center.
            XYZ down = position - (up - position);

            // Create two arcs that together form a full circle.
            Arc arc1 = Arc.Create(right, left, up);
            Arc arc2 = Arc.Create(left, right, down);

            // Add the arcs as model curves to the document so they become part of the model.
            doc.Create.NewModelCurve(arc1, sketchPlane);
            doc.Create.NewModelCurve(arc2, sketchPlane);
        }

        // This method defines connectivity (edges) between every pair of spaces.
        // It asks the user to input a connection strength and whether they prefer the spaces to be adjacent.
        // The adjacency doesn't change anything but COULD BE USED LATER IF NEEDED.
        private List<Edge> DefineConnectivityBetweenSpaces(List<SpaceNode> spaces)
        {
            List<Edge> edges = new List<Edge>();
            // Loop through each pair of spaces without repeating combinations.
            for (int i = 0; i < spaces.Count; i++)
            {
                for (int j = i + 1; j < spaces.Count; j++)
                {
                    // Ask the user for a numerical value representing the connection strength.
                    string weightInput = Interaction.InputBox(
                        $"Enter connection strength (edge weight) for {spaces[i].Name} and {spaces[j].Name}:",
                        "Edge Weight", "1");
                    double weight;
                    if (!double.TryParse(weightInput, out weight))
                        weight = 1; // Use default value if input is invalid.

                    // Ask the user if they prefer these two spaces to be adjacent.
                    string prefAdjInput = Interaction.InputBox(
                        $"Do you prefer adjacency between {spaces[i].Name} and {spaces[j].Name}? (Y/N)",
                        "Preferred Adjacency", "N");
                    bool preferredAdjacency = prefAdjInput.Trim().ToUpper() == "Y";

                    // Create a new Edge object for the two spaces and add it to the list.
                    edges.Add(new Edge(spaces[i], spaces[j], weight, preferredAdjacency));
                }
            }
            return edges;
        }

        // This method creates a line (model curve) between two points (start and end) in the document.
        private void CreateEdgeLine(Document doc, XYZ start, XYZ end)
        {
            // Calculate the midpoint between the start and end points to help position the plane.
            XYZ midPoint = (start + end) / 2.0;
            // Create a plane at the midpoint with the normal vector pointing up.
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, midPoint);
            // Create a sketch plane on which the line will be drawn.
            SketchPlane sp = SketchPlane.Create(doc, plane);

            // Create a line (model curve) between the start and end points.
            Line line = Line.CreateBound(start, end);
            // Add the line to the document.
            doc.Create.NewModelCurve(line, sp);
        }

        private double GetRadius(SpaceNode node)
        {
            double areaFt2 = node.Area * 10.7639;
            return Math.Sqrt(areaFt2 / Math.PI);
        }

        // Apply a force-directed layout to adjust the positions.
        private void PerformForceDirectedLayout(
            List<SpaceNode> spaces,
            List<Edge> edges,
            int iterations = 100,
            double kAttract = 0.05,
            double kRepulse = 0.05,
            double damping = 0.5)
        {
            // Build an adjacency map for quick lookup.
            var adjacencyMap = new Dictionary<(SpaceNode, SpaceNode), bool>();
            foreach (var e in edges)
            {
                adjacencyMap[(e.NodeA, e.NodeB)] = true;
                adjacencyMap[(e.NodeB, e.NodeA)] = true;
            }

            // Iteratively update positions.
            for (int iter = 0; iter < iterations; iter++)
            {
                XYZ[] netForces = new XYZ[spaces.Count];
                for (int i = 0; i < netForces.Length; i++)
                {
                    netForces[i] = XYZ.Zero;
                }

                // Calculate forces between each pair.
                for (int i = 0; i < spaces.Count; i++)
                {
                    var nodeA = spaces[i];
                    double rA = GetRadius(nodeA);

                    for (int j = i + 1; j < spaces.Count; j++)
                    {
                        var nodeB = spaces[j];
                        double rB = GetRadius(nodeB);

                        XYZ delta = nodeB.Position - nodeA.Position;
                        double dist = delta.GetLength();
                        if (dist < 1e-9)
                            dist = 1e-9; // avoid division by zero
                        XYZ direction = delta / dist;

                        bool connected = adjacencyMap.ContainsKey((nodeA, nodeB));
                        double desiredDist = rA + rB;

                        // Repulsion: push apart if circles overlap.
                        if (dist < desiredDist)
                        {
                            double overlap = desiredDist - dist;
                            double repulseForce = kRepulse * overlap;
                            netForces[i] -= repulseForce * direction;
                            netForces[j] += repulseForce * direction;
                        }

                        // Attraction: if connected, pull them toward being tangent.
                        if (connected)
                        {
                            double springExtension = dist - desiredDist;
                            double attractForce = kAttract * springExtension;
                            netForces[i] += attractForce * direction;
                            netForces[j] -= attractForce * direction;
                        }
                    }
                }

                // Update positions using the net force and damping.
                for (int i = 0; i < spaces.Count; i++)
                {
                    spaces[i].Position = spaces[i].Position + netForces[i] * damping;
                }
            }
        }
    }
}