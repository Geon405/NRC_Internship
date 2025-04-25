using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PanelizedAndModularFinal
{
    public class CellAssigner
    {
        public class CellAssignment
        {
            public ModuleGridCell Cell { get; set; }
            public SpaceNode Space { get; set; }
            public double CoveredArea { get; set; }
        }

        // maps cellIndex → list of (space, coveredArea)
        private readonly Dictionary<int, List<(SpaceNode Space, double CoveredArea)>> _coverageMap;
        private readonly double[] _cellAreas;
        private readonly List<SpaceNode> _spaces;
        private readonly List<ModuleGridCell> _cells;

        public CellAssigner(
            List<ModuleGridCell> cells,
            double[] cellAreas,
            List<GridTrimmer.TrimResult> trims,
            List<SpaceNode> spaces)
        {
            _cells = cells;
            _cellAreas = cellAreas;
            _spaces = spaces;

            // Build coverage map from trims:
            // TrimmedArea = cellArea - insideArea
            _coverageMap = new Dictionary<int, List<(SpaceNode, double)>>();
            foreach (var t in trims)
            {
                double inside = _cellAreas[t.CellIndex] - t.TrimmedArea;
                if (!_coverageMap.ContainsKey(t.CellIndex))
                    _coverageMap[t.CellIndex] = new List<(SpaceNode, double)>();
                _coverageMap[t.CellIndex].Add((t.Space, inside));
            }
        }

        /// <summary>
        /// Runs Step 1 & Step 2 and returns a list of final assignments.
        /// Also updates each SpaceNode.SquareTrimmedArea.
        /// </summary>
        public List<CellAssignment> AssignAll()
        {
            // track which cells still need assignment
            var remaining = new HashSet<int>(_coverageMap.Keys);
            // final cell→space assignments
            var assignments = new Dictionary<int, (SpaceNode, double)>();

            AssignSingleColorCells(remaining, assignments);
            AssignMultiColorCells(remaining, assignments);

            // build result list
            return assignments
                .Select(kv => new CellAssignment
                {
                    Cell = _cells[kv.Key],
                    Space = kv.Value.Item1,
                    CoveredArea = kv.Value.Item2
                })
                .ToList();
        }

        private void AssignSingleColorCells(
            HashSet<int> remaining,
            Dictionary<int, (SpaceNode, double)> assignments)
        {
            // cells with exactly one coverage source
            var singleCells = _coverageMap
                .Where(kvp => kvp.Value.Count == 1)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var space in _spaces)
            {
                // while this space still has trimmedArea > 0
                // and there remain single‐coverage cells for it:
                bool changed;
                do
                {
                    changed = false;
                    // pick among remaining single cells that belong to this space
                    var candidates = singleCells
                        .Where(ci => remaining.Contains(ci))
                        .Select(ci => (CellIndex: ci, Covered: _coverageMap[ci][0].CoveredArea))
                        .Where(tuple => _coverageMap[tuple.CellIndex][0].Space == space
                                        && tuple.Covered > 0
                                        && tuple.Covered < _cellAreas[tuple.CellIndex])
                        .OrderByDescending(tuple => tuple.Covered)
                        .ToList();

                    if (space.SquareTrimmedArea > 0 && candidates.Any())
                    {
                        var best = candidates.First();
                        // assign it
                        assignments[best.CellIndex] = (space, best.Covered);
                        remaining.Remove(best.CellIndex);
                        space.SquareTrimmedArea -= best.Covered;
                        changed = true;
                    }
                }
                while (changed);
            }
        }

        private void AssignMultiColorCells(
            HashSet<int> remaining,
            Dictionary<int, (SpaceNode, double)> assignments)
        {
            // cells with two or more coverage sources
            while (true)
            {
                var multi = remaining
                    .Where(ci => _coverageMap[ci].Count >= 2)
                    .FirstOrDefault();

                if (multi == 0 && !remaining.Any(ci => _coverageMap[ci].Count >= 2))
                    break;

                // choose that cell
                var coverList = _coverageMap[multi];
                // if any space still has trimmedArea>0, pick the one with largest trimmedArea
                var withLeft = coverList.Where(c => c.Space.SquareTrimmedArea > 0).ToList();
                (SpaceNode Space, double CoveredArea) chosen;

                if (withLeft.Any())
                {
                    chosen = withLeft
                        .OrderByDescending(c => c.Space.SquareTrimmedArea)
                        .First();
                }
                else
                {
                    // otherwise pick the one covering most of that cell
                    chosen = coverList
                        .OrderByDescending(c => c.CoveredArea)
                        .First();
                }

                // assign
                assignments[multi] = (chosen.Space, chosen.CoveredArea);
                chosen.Space.SquareTrimmedArea -= chosen.CoveredArea;
                remaining.Remove(multi);
            }
        }

        /// <summary>
        /// Pops up a TaskDialog showing for each cell which space it ended up in.
        /// </summary>
        public void ShowAssignments(List<CellAssignment> result)
        {
            var lines = new List<string>();
            lines.Add("Cell Assignments:");
            foreach (var a in result)
            {
                lines.Add(
                    $" Cell #{a.Cell.GlobalIndex}: " +
                    $"{a.Space.Name} (covered {a.CoveredArea:F2})"
                );
            }
            TaskDialog.Show("Cell Assigner Results", string.Join("\n", lines));
        }
    }
}
