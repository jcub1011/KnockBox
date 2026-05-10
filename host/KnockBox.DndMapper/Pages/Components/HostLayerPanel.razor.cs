using KnockBox.Core.Components.Shared;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Text;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class HostLayerPanel : DisposableComponent
    {
        [Parameter]
        public DndMapperGameState State { get; set; } = default;

        private Map? ActiveMap 
        { 
            get {
                if (State?.ActiveMapId is null) return null;
                return State.Maps.FirstOrDefault(map => map!.Id == State.ActiveMapId, null);
            }
        }
    }
}
