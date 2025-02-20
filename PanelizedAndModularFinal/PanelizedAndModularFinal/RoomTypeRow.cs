using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PanelizedAndModularFinal
{
    public class RoomTypeRow
    {
        public string Name { get; set; }       // e.g. "Bedroom"
        public Color Color { get; set; }       // WPF color
        public int Quantity { get; set; }      // # of spaces requested
    }
}
