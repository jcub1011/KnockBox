using System.Collections.Generic;
using System.Linq;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.Data;

public enum Wing { GrandHall, ModernWing, SculptureGarden, RestorationRoom, Corridor }
public enum SpotType { Curation, Event }

public record BoardSpace(int Id, string Name, Wing Wing, SpotType SpotType);

public class BoardGraph
{
    public IReadOnlyDictionary<int, BoardSpace> Spaces { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<int>> Adjacency { get; }

    public BoardGraph(Dictionary<int, BoardSpace> spaces, Dictionary<int, IReadOnlyList<int>> adjacency)
    {
        Spaces = spaces;
        Adjacency = adjacency;
    }

    public List<BoardSpace> GetReachableSpaces(int fromSpaceId, int maxDistance)
    {
        var reachable = new HashSet<int>();
        var queue = new Queue<(int Id, int Distance)>();
        
        queue.Enqueue((fromSpaceId, 0));
        var visited = new HashSet<int> { fromSpaceId };

        while (queue.Count > 0)
        {
            var (currentId, currentDistance) = queue.Dequeue();

            if (currentDistance > 0 && currentDistance <= maxDistance)
            {
                reachable.Add(currentId);
            }

            if (currentDistance < maxDistance)
            {
                if (Adjacency.TryGetValue(currentId, out var neighbors))
                {
                    foreach (var neighbor in neighbors)
                    {
                        if (!visited.Contains(neighbor))
                        {
                            visited.Add(neighbor);
                            queue.Enqueue((neighbor, currentDistance + 1));
                        }
                    }
                }
            }
        }

        return reachable.Select(id => Spaces[id]).ToList();
    }

    public int GetShortestDistance(int from, int to)
    {
        if (from == to) return 0;

        var queue = new Queue<(int Id, int Distance)>();
        queue.Enqueue((from, 0));
        var visited = new HashSet<int> { from };

        while (queue.Count > 0)
        {
            var (currentId, currentDistance) = queue.Dequeue();

            if (currentId == to) return currentDistance;

            if (Adjacency.TryGetValue(currentId, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue((neighbor, currentDistance + 1));
                    }
                }
            }
        }

        return -1; // Not reachable
    }
}
