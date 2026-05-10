using KnockBox.Core.Components.Shared;
using KnockBox.DndMapper.Services.Logic.Visibility;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class RollLogPanel : DisposableComponent
    {
        private const int MaxEntries = 20;

        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public string CurrentUserId { get; set; } = string.Empty;
        [Parameter] public bool IsHost { get; set; }

        private IDisposable? _stateSub;

        private List<RollResult> Visible
        {
            get
            {
                var filtered = RollLogVisibilityFilter.VisibleTo(
                    State.RollLog, CurrentUserId, IsHost, State.Settings.RollsVisibleToPlayers);
                var list = filtered.ToList();
                if (list.Count > MaxEntries)
                {
                    list = list.GetRange(list.Count - MaxEntries, MaxEntries);
                }
                return list;
            }
        }

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        private string RollerName(string rollerUserId)
        {
            if (State.Host.Id == rollerUserId) return State.Host.Name;
            var entry = State.Players.FirstOrDefault(p => p.User.Id == rollerUserId);
            return entry.User is null ? "?" : entry.DisplayName;
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
