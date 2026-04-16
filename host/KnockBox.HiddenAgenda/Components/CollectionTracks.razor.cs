using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.HiddenAgenda.Components
{
    public partial class CollectionTracks : ComponentBase
    {
        [Parameter, EditorRequired] public Dictionary<CollectionType, int> Progress { get; set; } = default!;

        private static int GetTarget(CollectionType type) => type switch
        {
            CollectionType.RenaissanceMasters => 12,
            CollectionType.ContemporaryShowcase => 10,
            CollectionType.ImpressionistGallery => 10,
            CollectionType.MarbleAndBronze => 8,
            CollectionType.EmergingArtists => 8,
            _ => 10
        };
    }
}
