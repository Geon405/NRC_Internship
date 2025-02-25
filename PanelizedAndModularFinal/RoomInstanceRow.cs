using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PanelizedAndModularFinal
{
    public class RoomInstanceRow
    {
        public string RoomType { get; set; }          // e.g. "Bedroom"
        public string Name { get; set; }              // e.g. "Bedroom 1"
        public Color WpfColor { get; set; }           // The color from the original row
        public double Area { get; set; }              // Editable area (ft²)
    }
}