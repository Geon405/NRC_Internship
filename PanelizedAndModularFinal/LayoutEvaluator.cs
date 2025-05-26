using System;
using System.Collections.Generic;
using System.Linq;
using PanelizedAndModularFinal;

namespace FloorplanEvaluation
{
    /// <summary>
    /// Provides methods to evaluate a completed layout against
    /// space-type coverage and size requirements.
    /// </summary>
    public static class LayoutEvaluator
    {
        /// <summary>
        /// Builds a map of total assigned area per space type
        /// by scanning each painted cell.
        /// </summary>
        /// <param name="paintedCells">
        /// Dictionary mapping ModuleGridCell → owning SpaceNode
        /// from Phase 3 results.
        /// </param>
        /// <returns>
        /// Dictionary of space type name → total assigned area.
        /// </returns>
        public static IDictionary<string, double> BuildAssignedAreaMap(
            IDictionary<ModuleGridCell, SpaceNode> paintedCells)
        {
            var areaMap = new Dictionary<string, double>();
            foreach (var kv in paintedCells)
            {
                var cell = kv.Key;
                var room = kv.Value.Name;
                double area = cell.Size * cell.Size;

                if (!areaMap.ContainsKey(room))
                    areaMap[room] = 0;
                areaMap[room] += area;
            }
            return areaMap;
        }

        /// <summary>
        /// Checks that every original space type appears at least once
        /// in the final assigned areas.
        /// </summary>
        public static bool CheckSpaceTypeCoverage(
            IEnumerable<string> originalSpaceTypes,
            IDictionary<string, double> finalAssignedAreas)
        {
            return originalSpaceTypes
                .All(type =>
                    finalAssignedAreas.ContainsKey(type) &&
                    finalAssignedAreas[type] > 0);
        }

        /// <summary>
        /// Computes the total size penalty for any spaces
        /// that fall short of their required area.
        /// </summary>
        public static double ComputeSizePenalty(
            IEnumerable<string> allSpaceTypes,
            IDictionary<string, double> finalAssignedAreas,
            IDictionary<string, double> requiredAreas)
        {
            double totalPenalty = 0;

            foreach (var type in allSpaceTypes)
            {
                finalAssignedAreas.TryGetValue(type, out double actual);
                requiredAreas.TryGetValue(type, out double required);

                if (required > 0 && actual < required)
                    totalPenalty += (required - actual) / required;
            }

            return totalPenalty;
        }
    }
}
