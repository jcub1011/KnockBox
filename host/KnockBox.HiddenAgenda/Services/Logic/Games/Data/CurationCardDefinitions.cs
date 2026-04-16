using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Services.Logic.RandomGeneration;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.Data;

public enum CurationCardType { Acquire, Remove, Trade }

public record CollectionEffect(CollectionType Collection, int Delta);

public record CurationCard(
    CurationCardType Type,
    string Description,
    IReadOnlyList<CollectionEffect> Effects,
    IReadOnlyList<CollectionEffect>? AlternateEffects = null
);

public static class CurationCardPool
{
    public static IReadOnlyList<CurationCard> GetPool(Wing wing)
    {
        return wing switch
        {
            Wing.GrandHall => GetGrandHallPool(),
            Wing.ModernWing => GetModernWingPool(),
            Wing.SculptureGarden => GetSculptureGardenPool(),
            Wing.RestorationRoom => GetRestorationRoomPool(),
            _ => new List<CurationCard>()
        };
    }

    public static List<CurationCard> DrawThree(IRandomNumberService rng, Wing wing)
    {
        var pool = GetPool(wing);
        var drawn = new List<CurationCard>();
        for (int i = 0; i < 3; i++)
        {
            int index = rng.GetRandomInt(pool.Count);
            drawn.Add(pool[index]);
        }
        return drawn;
    }

    private static List<CurationCard> GetGrandHallPool()
    {
        return new List<CurationCard>
        {
            // Acquire (50-60%)
            new(CurationCardType.Acquire, "+2 Renaissance Masters", [new(CollectionType.RenaissanceMasters, 2)]),
            new(CurationCardType.Acquire, "+2 Contemporary Showcase", [new(CollectionType.ContemporaryShowcase, 2)]),
            new(CurationCardType.Acquire, "+1 Renaissance Masters, +1 Contemporary Showcase", [new(CollectionType.RenaissanceMasters, 1), new(CollectionType.ContemporaryShowcase, 1)]),
            new(CurationCardType.Acquire, "+3 Renaissance Masters", [new(CollectionType.RenaissanceMasters, 3)]),
            new(CurationCardType.Acquire, "+1 Renaissance Masters", [new(CollectionType.RenaissanceMasters, 1)]),
            new(CurationCardType.Acquire, "+1 Contemporary Showcase", [new(CollectionType.ContemporaryShowcase, 1)]),
            new(CurationCardType.Acquire, "+1 Renaissance Masters, +1 Impressionist Gallery", [new(CollectionType.RenaissanceMasters, 1), new(CollectionType.ImpressionistGallery, 1)]),
            new(CurationCardType.Acquire, "+1 Contemporary Showcase, +1 Emerging Artists", [new(CollectionType.ContemporaryShowcase, 1), new(CollectionType.EmergingArtists, 1)]),
            new(CurationCardType.Acquire, "+2 to any Grand Hall collection", [new(CollectionType.RenaissanceMasters, 2)]), // Simple logic for "any", actual choice handled by UI/Engine
            new(CurationCardType.Acquire, "+1 to all Grand Hall collections", [new(CollectionType.RenaissanceMasters, 1), new(CollectionType.ContemporaryShowcase, 1)]),

            // Remove (15-20%)
            new(CurationCardType.Remove, "-2 Renaissance Masters", [new(CollectionType.RenaissanceMasters, -2)]),
            new(CurationCardType.Remove, "-2 Contemporary Showcase", [new(CollectionType.ContemporaryShowcase, -2)]),
            new(CurationCardType.Remove, "-1 Renaissance Masters, -1 Contemporary Showcase", [new(CollectionType.RenaissanceMasters, -1), new(CollectionType.ContemporaryShowcase, -1)]),

            // Trade (20-30%)
            new(CurationCardType.Trade, "+2 Renaissance Masters OR +1 Impressionist Gallery and +1 Emerging Artists", 
                [new(CollectionType.RenaissanceMasters, 2)], 
                [new(CollectionType.ImpressionistGallery, 1), new(CollectionType.EmergingArtists, 1)]),
            new(CurationCardType.Trade, "+2 Contemporary Showcase OR +2 Marble & Bronze", 
                [new(CollectionType.ContemporaryShowcase, 2)], 
                [new(CollectionType.MarbleAndBronze, 2)]),
            new(CurationCardType.Trade, "+1 Renaissance Masters, +1 Contemporary Showcase OR +3 Renaissance Masters", 
                [new(CollectionType.RenaissanceMasters, 1), new(CollectionType.ContemporaryShowcase, 1)], 
                [new(CollectionType.RenaissanceMasters, 3)]),
            new(CurationCardType.Trade, "+2 Renaissance Masters OR -1 Contemporary Showcase and +3 Renaissance Masters", 
                [new(CollectionType.RenaissanceMasters, 2)], 
                [new(CollectionType.ContemporaryShowcase, -1), new(CollectionType.RenaissanceMasters, 3)])
        };
    }

    private static List<CurationCard> GetModernWingPool()
    {
        return new List<CurationCard>
        {
            // Acquire
            new(CurationCardType.Acquire, "+2 Impressionist Gallery", [new(CollectionType.ImpressionistGallery, 2)]),
            new(CurationCardType.Acquire, "+3 Impressionist Gallery", [new(CollectionType.ImpressionistGallery, 3)]),
            new(CurationCardType.Acquire, "+1 Impressionist Gallery, +1 Renaissance Masters", [new(CollectionType.ImpressionistGallery, 1), new(CollectionType.RenaissanceMasters, 1)]),
            new(CurationCardType.Acquire, "+1 Impressionist Gallery, +1 Emerging Artists", [new(CollectionType.ImpressionistGallery, 1), new(CollectionType.EmergingArtists, 1)]),
            new(CurationCardType.Acquire, "+2 Impressionist Gallery, +1 Contemporary Showcase", [new(CollectionType.ImpressionistGallery, 2), new(CollectionType.ContemporaryShowcase, 1)]),
            new(CurationCardType.Acquire, "+1 Impressionist Gallery", [new(CollectionType.ImpressionistGallery, 1)]),
            new(CurationCardType.Acquire, "+2 to any Modern Wing collection (Impressionist)", [new(CollectionType.ImpressionistGallery, 2)]),

            // Remove
            new(CurationCardType.Remove, "-2 Impressionist Gallery", [new(CollectionType.ImpressionistGallery, -2)]),
            new(CurationCardType.Remove, "-1 Impressionist Gallery, -1 Renaissance Masters", [new(CollectionType.ImpressionistGallery, -1), new(CollectionType.RenaissanceMasters, -1)]),
            new(CurationCardType.Remove, "-3 Impressionist Gallery", [new(CollectionType.ImpressionistGallery, -3)]),

            // Trade
            new(CurationCardType.Trade, "+2 Impressionist Gallery OR +2 Renaissance Masters", 
                [new(CollectionType.ImpressionistGallery, 2)], 
                [new(CollectionType.RenaissanceMasters, 2)]),
            new(CurationCardType.Trade, "+3 Impressionist Gallery OR +1 Renaissance Masters, +1 Contemporary Showcase", 
                [new(CollectionType.ImpressionistGallery, 3)], 
                [new(CollectionType.RenaissanceMasters, 1), new(CollectionType.ContemporaryShowcase, 1)]),
            new(CurationCardType.Trade, "+2 Impressionist Gallery OR -1 Impressionist Gallery and +4 Renaissance Masters", 
                [new(CollectionType.ImpressionistGallery, 2)], 
                [new(CollectionType.ImpressionistGallery, -1), new(CollectionType.RenaissanceMasters, 4)])
        };
    }

    private static List<CurationCard> GetSculptureGardenPool()
    {
        return new List<CurationCard>
        {
            // Acquire
            new(CurationCardType.Acquire, "+2 Marble & Bronze", [new(CollectionType.MarbleAndBronze, 2)]),
            new(CurationCardType.Acquire, "+2 Emerging Artists", [new(CollectionType.EmergingArtists, 2)]),
            new(CurationCardType.Acquire, "+1 Marble & Bronze, +1 Emerging Artists", [new(CollectionType.MarbleAndBronze, 1), new(CollectionType.EmergingArtists, 1)]),
            new(CurationCardType.Acquire, "+3 Marble & Bronze", [new(CollectionType.MarbleAndBronze, 3)]),
            new(CurationCardType.Acquire, "+3 Emerging Artists", [new(CollectionType.EmergingArtists, 3)]),
            new(CurationCardType.Acquire, "+1 Marble & Bronze", [new(CollectionType.MarbleAndBronze, 1)]),
            new(CurationCardType.Acquire, "+1 Emerging Artists", [new(CollectionType.EmergingArtists, 1)]),
            new(CurationCardType.Acquire, "+1 Marble & Bronze, +1 Impressionist Gallery", [new(CollectionType.MarbleAndBronze, 1), new(CollectionType.ImpressionistGallery, 1)]),

            // Remove
            new(CurationCardType.Remove, "-2 Marble & Bronze", [new(CollectionType.MarbleAndBronze, -2)]),
            new(CurationCardType.Remove, "-2 Emerging Artists", [new(CollectionType.EmergingArtists, -2)]),
            new(CurationCardType.Remove, "-1 Marble & Bronze, -1 Emerging Artists", [new(CollectionType.MarbleAndBronze, -1), new(CollectionType.EmergingArtists, -1)]),

            // Trade
            new(CurationCardType.Trade, "+2 Marble & Bronze OR +2 Emerging Artists", 
                [new(CollectionType.MarbleAndBronze, 2)], 
                [new(CollectionType.EmergingArtists, 2)]),
            new(CurationCardType.Trade, "+3 Marble & Bronze OR +1 Impressionist Gallery, +1 Renaissance Masters", 
                [new(CollectionType.MarbleAndBronze, 3)], 
                [new(CollectionType.ImpressionistGallery, 1), new(CollectionType.RenaissanceMasters, 1)]),
            new(CurationCardType.Trade, "+1 Marble & Bronze, +1 Emerging Artists OR +2 Renaissance Masters", 
                [new(CollectionType.MarbleAndBronze, 1), new(CollectionType.EmergingArtists, 1)], 
                [new(CollectionType.RenaissanceMasters, 2)])
        };
    }

    private static List<CurationCard> GetRestorationRoomPool()
    {
        return new List<CurationCard>
        {
            // Acquire (More flexible, lower values)
            new(CurationCardType.Acquire, "+1 to any collection", [new(CollectionType.RenaissanceMasters, 1)]),
            new(CurationCardType.Acquire, "+1 Renaissance Masters, +1 Marble & Bronze", [new(CollectionType.RenaissanceMasters, 1), new(CollectionType.MarbleAndBronze, 1)]),
            new(CurationCardType.Acquire, "+1 Contemporary Showcase, +1 Emerging Artists", [new(CollectionType.ContemporaryShowcase, 1), new(CollectionType.EmergingArtists, 1)]),
            new(CurationCardType.Acquire, "+1 Impressionist Gallery, +1 Marble & Bronze", [new(CollectionType.ImpressionistGallery, 1), new(CollectionType.MarbleAndBronze, 1)]),
            new(CurationCardType.Acquire, "+1 Renaissance Masters, +1 Contemporary Showcase, +1 Impressionist Gallery", [new(CollectionType.RenaissanceMasters, 1), new(CollectionType.ContemporaryShowcase, 1), new(CollectionType.ImpressionistGallery, 1)]),

            // Remove
            new(CurationCardType.Remove, "-1 to any collection", [new(CollectionType.RenaissanceMasters, -1)]),
            new(CurationCardType.Remove, "-1 Renaissance Masters, -1 Contemporary Showcase", [new(CollectionType.RenaissanceMasters, -1), new(CollectionType.ContemporaryShowcase, -1)]),

            // Trade
            new(CurationCardType.Trade, "+2 to any collection OR +1 to all collections", 
                [new(CollectionType.RenaissanceMasters, 2)], 
                [new(CollectionType.RenaissanceMasters, 1), new(CollectionType.ContemporaryShowcase, 1), new(CollectionType.ImpressionistGallery, 1), new(CollectionType.MarbleAndBronze, 1), new(CollectionType.EmergingArtists, 1)]),
            new(CurationCardType.Trade, "+1 Renaissance Masters OR +1 Contemporary Showcase", 
                [new(CollectionType.RenaissanceMasters, 1)], 
                [new(CollectionType.ContemporaryShowcase, 1)]),
            new(CurationCardType.Trade, "+1 Impressionist Gallery OR +1 Marble & Bronze", 
                [new(CollectionType.ImpressionistGallery, 1)], 
                [new(CollectionType.MarbleAndBronze, 1)])
        };
    }
}
