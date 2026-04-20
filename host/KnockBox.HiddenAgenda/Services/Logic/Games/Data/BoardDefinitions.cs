using System.Collections.Generic;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.Data;

public static class BoardDefinitions
{
    public static BoardGraph CreateGrandCircuit()
    {
        var spaces = new Dictionary<int, BoardSpace>();
        var adj = new Dictionary<int, IReadOnlyList<int>>();

        // Main Loop: 20 spaces (0-19)
        // Grand Hall (0-4)
        AddSpace(spaces, 0, "Grand Hall Foyer", Wing.GrandHall, SpotType.Curation);
        AddSpace(spaces, 1, "Renaissance Hall", Wing.GrandHall, SpotType.Curation);
        AddSpace(spaces, 2, "Grand Staircase", Wing.GrandHall, SpotType.Curation);
        AddSpace(spaces, 3, "Baroque Gallery", Wing.GrandHall, SpotType.Curation);
        AddSpace(spaces, 4, "Grand Hall Event Space", Wing.GrandHall, SpotType.Event);

        // Modern Wing (5-9)
        AddSpace(spaces, 5, "Abstract Atrium", Wing.ModernWing, SpotType.Curation);
        AddSpace(spaces, 6, "Pop Art Plaza", Wing.ModernWing, SpotType.Curation);
        AddSpace(spaces, 7, "Modernist Corridor", Wing.ModernWing, SpotType.Curation);
        AddSpace(spaces, 8, "Surrealist Salon", Wing.ModernWing, SpotType.Curation);
        AddSpace(spaces, 9, "Modern Wing Event Space", Wing.ModernWing, SpotType.Event);

        // Sculpture Garden (10-14)
        AddSpace(spaces, 10, "Bronze Terrace", Wing.SculptureGarden, SpotType.Curation);
        AddSpace(spaces, 11, "Marble Walkway", Wing.SculptureGarden, SpotType.Curation);
        AddSpace(spaces, 12, "Zen Fountain", Wing.SculptureGarden, SpotType.Curation);
        AddSpace(spaces, 13, "Statue Grove", Wing.SculptureGarden, SpotType.Curation);
        AddSpace(spaces, 14, "Garden Event Space", Wing.SculptureGarden, SpotType.Event);

        // Restoration Room (15-19)
        AddSpace(spaces, 15, "Archive Alcove", Wing.RestorationRoom, SpotType.Curation);
        AddSpace(spaces, 16, "Preservation Lab", Wing.RestorationRoom, SpotType.Curation);
        AddSpace(spaces, 17, "Chemical Storage", Wing.RestorationRoom, SpotType.Curation);
        AddSpace(spaces, 18, "Unfinished Works", Wing.RestorationRoom, SpotType.Curation);
        AddSpace(spaces, 19, "Restoration Event Space", Wing.RestorationRoom, SpotType.Event);

        // Shortcuts (20-23)
        AddSpace(spaces, 20, "North Corridor", Wing.Corridor, SpotType.Curation);
        AddSpace(spaces, 21, "South Corridor", Wing.Corridor, SpotType.Curation);
        AddSpace(spaces, 22, "East Corridor", Wing.Corridor, SpotType.Curation);
        AddSpace(spaces, 23, "West Corridor", Wing.Corridor, SpotType.Curation);

        // Build Adjacency
        // Main Loop
        for (int i = 0; i < 20; i++)
        {
            AddEdge(adj, i, (i + 1) % 20);
        }

        // Shortcut 1: GH-SG (2 <-> 20 <-> 21 <-> 12)
        AddEdge(adj, 2, 20);
        AddEdge(adj, 20, 21);
        AddEdge(adj, 21, 12);

        // Shortcut 2: MW-RR (7 <-> 22 <-> 23 <-> 17)
        AddEdge(adj, 7, 22);
        AddEdge(adj, 22, 23);
        AddEdge(adj, 23, 17);

        return new BoardGraph(spaces, adj);
    }

    private static void AddSpace(Dictionary<int, BoardSpace> spaces, int id, string name, Wing wing, SpotType spotType)
    {
        spaces[id] = new BoardSpace(id, name, wing, spotType);
    }

    private static void AddEdge(Dictionary<int, IReadOnlyList<int>> adj, int u, int v)
    {
        AddDirectionalEdge(adj, u, v);
        AddDirectionalEdge(adj, v, u);
    }

    private static void AddDirectionalEdge(Dictionary<int, IReadOnlyList<int>> adj, int u, int v)
    {
        if (!adj.TryGetValue(u, out var list))
        {
            list = new List<int>();
            adj[u] = list;
        }
        ((List<int>)list).Add(v);
    }
}
