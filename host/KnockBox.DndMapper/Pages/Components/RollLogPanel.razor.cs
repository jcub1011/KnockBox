using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
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
        [Parameter, EditorRequired] public DiceRollerConfig Config { get; set; } = default!;
        [Parameter] public string CurrentUserId { get; set; } = string.Empty;
        [Parameter] public bool IsHost { get; set; }
        [Parameter] public EventCallback OnOpenSettings { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

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

        private string QuickRollTitle
        {
            get
            {
                var dice = string.Join(" + ",
                    Config.Terms.Where(t => t.Count > 0).Select(t => $"{t.Count}d{t.Sides}"));
                if (string.IsNullOrEmpty(dice)) dice = "—";
                var mod = Config.FlatModifier == 0 ? string.Empty
                    : (Config.FlatModifier > 0 ? $" +{Config.FlatModifier}" : $" {Config.FlatModifier}");
                return $"Roll: {dice}{mod}";
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

        private Task QuickRoll() =>
            DiceRollSubmitter.SubmitAsync(Engine, State, UserService.CurrentUser, Config, Toasts);

        private Task OpenSettings() => OnOpenSettings.InvokeAsync();

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
