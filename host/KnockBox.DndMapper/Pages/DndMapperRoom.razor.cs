using KnockBox.Core.Components.Shared;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages
{
    public partial class DndMapperRoom : LobbyPageBase<DndMapperGameState>
    {
        [Inject] protected DndMapperGameEngine GameEngine { get; set; } = default!;
    }
}
