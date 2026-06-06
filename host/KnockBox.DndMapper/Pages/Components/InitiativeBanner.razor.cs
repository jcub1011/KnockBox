using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Services.Logic;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class InitiativeBanner : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public Guid CurrentUserId { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected IDiceAnimationTracker Tracker { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;
        private Action? _trackerSub;

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            // Re-render when the 3D dice settle for any roll so the gated
            // initiative cell reveals the value at exactly that moment.
            _trackerSub = () => _ = InvokeAsync(StateHasChanged);
            Tracker.Changed += _trackerSub;
            base.OnInitialized();
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            if (_trackerSub is not null) Tracker.Changed -= _trackerSub;
            base.Dispose();
        }

        // Hide the player banner's roll value while either the dice are still
        // tumbling for this token, or the host has staged a value via SetNpc
        // and the dice haven't fired yet (PendingInitiative). Mirrors the
        // host panel so the players don't see the manually-typed value an
        // instant before the dice land.
        private bool IsInitiativeAnimating(CombatantEntry entry) =>
            entry.PendingInitiative is not null
            || InitiativeAnimationGate.IsAnimatingFor(State.RollLog, Tracker, entry);

        private async Task OnRollInitiative()
        {
            var user = UserService.CurrentUser;
            if (user is null) return;
            var result = Engine.SubmitInitiativeRollAsync(State, user);
            if (result.TryGetFailure(out var err) && Toasts is not null)
            {
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }
    }
}
