using KnockBox.Core.Components.Shared;
using KnockBox.DndMapper.Services.Logic;
using KnockBox.DndMapper.Services.Logic.Visibility;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    // The "all rolls" history modal. Snapshots the visible roll log at open
    // time (the hint tells the user it's frozen — reopen to refresh) so the
    // list doesn't churn under them as new rolls land. Shared by RollLogPanel
    // and the footer's recent-rolls popup.
    public partial class RollHistoryModal : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public string CurrentUserId { get; set; } = string.Empty;
        [Parameter] public bool IsHost { get; set; }
        [Parameter] public EventCallback OnClose { get; set; }

        [Inject] protected IDiceAnimationTracker Tracker { get; set; } = default!;

        private List<RollResult> _snapshot = [];
        private DateTime _capturedAt;

        protected override void OnInitialized()
        {
            var filtered = RollLogVisibilityFilter.VisibleTo(
                State.RollLog, CurrentUserId, IsHost, State.Settings.RollsVisibleToPlayers);
            var list = filtered.Where(r => !Tracker.IsAnimating(r.Id)).ToList();
            list.Reverse();
            _snapshot = list;
            _capturedAt = DateTime.Now;
            base.OnInitialized();
        }

        private Task Close() => OnClose.InvokeAsync();
    }
}
