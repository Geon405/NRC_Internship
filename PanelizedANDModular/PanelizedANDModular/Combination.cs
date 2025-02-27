using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Collections.Generic;

namespace PanelizedAndModularFinal
{
    // Helper class to store a combination of modules.
    public class Combination
    {
        // The key is the index of the module type (from ModuleType list)
        // and the value is the count used.
        public Dictionary<int, int> ModuleCounts { get; set; }
        public double TotalArea { get; set; }
    }
}

