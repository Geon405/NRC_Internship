using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace PanelizedAndModularFinal
{
    public class SpaceNode
    {
        // Static list to hold all SpaceNode instances.
        private static List<SpaceNode> _allSpaces = new List<SpaceNode>();

        // Public getter to access all SpaceNode instances.
        public static IReadOnlyList<SpaceNode> AllSpaces => _allSpaces.AsReadOnly();

        // Name of the room.
        public string Name { get; set; }
        // Function or type of the room.
        public string Function { get; set; }
        // Area of the room.
        public double Area { get; set; }
        // Position of the room in the model.
        public XYZ Position { get; set; }
        // Color used for displaying the room (WPF color).
        public System.Windows.Media.Color WpfColor { get; set; }
        // Radius value for the space.
        public double Radius { get; set; }

        // Constructor to initialize the space node with provided values.
        public SpaceNode(string name, string function, double area, XYZ position, System.Windows.Media.Color wpfColor)
        {
            Name = name;
            Function = function;
            Area = area;
            Position = position;
            WpfColor = wpfColor;
            Radius = 0.0;

            // Add this instance to the static list.
            _allSpaces.Add(this);
        }
    }
}
