using System.Globalization;
using System.Text;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Helpers
{
    public static class FogPolygonBuilder
    {
        public static IReadOnlyList<IReadOnlyList<(int X, int Y)>> Build(Map map)
        {
            ArgumentNullException.ThrowIfNull(map);
            if (map.FogMask.Length == 0) return Array.Empty<IReadOnlyList<(int X, int Y)>>();
            return TraceRings(map);
        }

        public static string BuildSvgPathData(Map map)
        {
            ArgumentNullException.ThrowIfNull(map);
            if (map.FogMask.Length == 0) return string.Empty;
            var rings = TraceRings(map);
            if (rings.Count == 0) return string.Empty;
            return FormatPath(rings);
        }

        private static List<IReadOnlyList<(int X, int Y)>> TraceRings(Map map)
        {
            var w = map.Grid.WidthCells;
            var h = map.Grid.HeightCells;

            var outgoing = new Dictionary<(int X, int Y), List<(int X, int Y)>>();

            void AddEdge((int X, int Y) from, (int X, int Y) to)
            {
                if (!outgoing.TryGetValue(from, out var list))
                {
                    list = new List<(int X, int Y)>(1);
                    outgoing[from] = list;
                }
                list.Add(to);
            }

            for (var cy = 0; cy < h; cy++)
            {
                for (var cx = 0; cx < w; cx++)
                {
                    if (!map.IsFogged(cx, cy)) continue;

                    if (!map.IsFogged(cx, cy - 1)) AddEdge((cx, cy), (cx + 1, cy));
                    if (!map.IsFogged(cx + 1, cy)) AddEdge((cx + 1, cy), (cx + 1, cy + 1));
                    if (!map.IsFogged(cx, cy + 1)) AddEdge((cx + 1, cy + 1), (cx, cy + 1));
                    if (!map.IsFogged(cx - 1, cy)) AddEdge((cx, cy + 1), (cx, cy));
                }
            }

            var rings = new List<IReadOnlyList<(int X, int Y)>>();
            var visited = new HashSet<((int X, int Y) From, (int X, int Y) To)>();

            foreach (var kvp in outgoing)
            {
                foreach (var dest in kvp.Value)
                {
                    var startEdge = (kvp.Key, dest);
                    if (visited.Contains(startEdge)) continue;

                    var ring = new List<(int X, int Y)> { kvp.Key };
                    var current = kvp.Key;
                    var next = dest;

                    while (true)
                    {
                        visited.Add((current, next));
                        ring.Add(next);
                        var prevDir = (next.X - current.X, next.Y - current.Y);
                        current = next;
                        if (current == kvp.Key) break;

                        next = ChooseNext(current, prevDir, outgoing, visited);
                    }

                    ring.RemoveAt(ring.Count - 1);
                    CollapseCollinear(ring);
                    rings.Add(ring);
                }
            }

            return rings;
        }

        // On a y-down screen the 2D cross product
        //   prevDir.dx * cand.dy - prevDir.dy * cand.dx
        // is POSITIVE for a right turn (clockwise visually). At a "figure-8"
        // saddle vertex we prefer the right-most turn so each ring stays self-
        // contained instead of crossing into a neighboring cluster.
        private static (int X, int Y) ChooseNext(
            (int X, int Y) at,
            (int dx, int dy) prevDir,
            Dictionary<(int X, int Y), List<(int X, int Y)>> outgoing,
            HashSet<((int X, int Y), (int X, int Y))> visited)
        {
            var options = outgoing[at];
            if (options.Count == 1) return options[0];

            (int X, int Y) best = options[0];
            var bestRank = int.MaxValue;
            foreach (var cand in options)
            {
                if (visited.Contains((at, cand))) continue;
                var dx = cand.X - at.X;
                var dy = cand.Y - at.Y;
                var cross = prevDir.dx * dy - prevDir.dy * dx;
                var dot = prevDir.dx * dx + prevDir.dy * dy;
                var rank = cross > 0 ? 0
                         : (cross == 0 && dot > 0) ? 1
                         : cross < 0 ? 2
                         : 3;
                if (rank < bestRank)
                {
                    bestRank = rank;
                    best = cand;
                }
            }
            return best;
        }

        private static void CollapseCollinear(List<(int X, int Y)> ring)
        {
            if (ring.Count < 3) return;
            var i = 0;
            while (i < ring.Count && ring.Count >= 3)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                var c = ring[(i + 2) % ring.Count];
                var collinear =
                    (a.X == b.X && b.X == c.X) ||
                    (a.Y == b.Y && b.Y == c.Y);
                if (collinear)
                {
                    ring.RemoveAt((i + 1) % ring.Count);
                    if (i > 0) i--;
                }
                else
                {
                    i++;
                }
            }
        }

        private static string FormatPath(IReadOnlyList<IReadOnlyList<(int X, int Y)>> rings)
        {
            var sb = new StringBuilder();
            for (var r = 0; r < rings.Count; r++)
            {
                var ring = rings[r];
                if (ring.Count == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append('M').Append(' ')
                  .Append(ring[0].X.ToString(CultureInfo.InvariantCulture)).Append(' ')
                  .Append(ring[0].Y.ToString(CultureInfo.InvariantCulture));
                for (var i = 1; i < ring.Count; i++)
                {
                    sb.Append(" L ")
                      .Append(ring[i].X.ToString(CultureInfo.InvariantCulture)).Append(' ')
                      .Append(ring[i].Y.ToString(CultureInfo.InvariantCulture));
                }
                sb.Append(" Z");
            }
            return sb.ToString();
        }
    }
}
