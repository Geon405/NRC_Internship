
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PanelizedAndModularFinal
{
    public static class GraphEvaluator
    {

        // Average Shortest Path Length on your room‐circle graph:
        public static double CalculateASPL(List<SpaceNode> spaces, int[,] connectivity)
        {
            int n = spaces.Count;
            double total = 0.0;

            for (int s = 0; s < n; s++)
            {
                var dist = Enumerable.Repeat(-1, n).ToArray();
                var q = new Queue<int>();
                dist[s] = 0;
                q.Enqueue(s);

                while (q.Count > 0)
                {
                    int u = q.Dequeue();
                    for (int v = 0; v < n; v++)
                        if (connectivity[u, v] == 1 && dist[v] < 0)
                        {
                            dist[v] = dist[u] + 1;
                            q.Enqueue(v);
                        }
                }

                for (int t = 0; t < n; t++)
                    if (t != s && dist[t] >= 0)
                        total += dist[t];
            }

            return total / (n * (n - 1));
        }

        // Simple density = 2m / n(n−1)
        // in GraphEvaluator.cs
        public static double CalculateDensity(IList<SpaceNode> spaces, int[,] connectivity)
        {
            int n = spaces.Count, m = 0;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (connectivity[i, j] == 1) m++;
            return 2.0 * m / (n * (n - 1));
        }
    }
}
