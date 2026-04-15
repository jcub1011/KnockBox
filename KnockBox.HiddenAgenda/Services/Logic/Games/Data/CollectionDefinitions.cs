using System.Collections.Generic;
using System.Linq;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.Data;

public enum CollectionType
{
    RenaissanceMasters,
    ContemporaryShowcase,
    ImpressionistGallery,
    MarbleAndBronze,
    EmergingArtists
}

public record CollectionDefinition(CollectionType Type, string Name, int TargetValue, Wing PrimaryWing);

public static class CollectionDefinitions
{
    public static IReadOnlyList<CollectionDefinition> All { get; } = new List<CollectionDefinition>
    {
        new(CollectionType.RenaissanceMasters, "Renaissance Masters", 12, Wing.GrandHall),
        new(CollectionType.ContemporaryShowcase, "Contemporary Showcase", 10, Wing.GrandHall),
        new(CollectionType.ImpressionistGallery, "Impressionist Gallery", 10, Wing.ModernWing),
        new(CollectionType.MarbleAndBronze, "Marble & Bronze", 8, Wing.SculptureGarden),
        new(CollectionType.EmergingArtists, "Emerging Artists", 8, Wing.SculptureGarden)
    };

    public static CollectionDefinition Get(CollectionType type) => All.First(c => c.Type == type);
}
