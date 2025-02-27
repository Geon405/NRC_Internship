using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PanelizedAndModularFinal
{
    public class ModuleType
    {
        public int ID { get; set; }
        public double Width { get; set; }
        public double Length { get; set; }
        public double Area { get; set; }

        public override string ToString()
        {
            return $"Module_Type {ID}: {Width} * {Length} = {Area}";
        }
    }
}

