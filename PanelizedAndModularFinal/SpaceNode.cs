using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;

namespace PanelizedAndModularFinal
{

    public class SpaceNode
    {
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
        }
    }
}