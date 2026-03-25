using KnockBox.Components.Shared;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Navigation;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Components.Pages.Games.DrawnToDress
{
    public partial class DrawnToDressLobby : DisposableComponent
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;

        [Inject] protected INavigationService NavigationService { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<DrawnToDressLobby> Logger { get; set; } = default!;

        [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

        private IDisposable? _stateSubscription;
        private PeriodicTimer? _timer;

        protected override async Task OnInitializedAsync()
        {
            if (UserService.CurrentUser is null)
                await UserService.InitializeCurrentUserAsync(ComponentDetached);

            if (!GameSessionService.TryGetCurrentSession(out var session))
            {
                Logger.LogWarning("User [{userId}] attempted to enter room [{code}] without a session set.",
                    UserService.CurrentUser?.Id ?? "Unknown", ObfuscatedRoomCode);
                NavigationService.ToHome();
                return;
            }

            if (!TryExtractObfuscatedRoomCode(session.LobbyRegistration.Uri, out var roomCode))
            {
                Logger.LogError("User [{userId}] attempted to enter room [{code}] but their session registration uri [{uri}] could not be parsed.",
                    UserService.CurrentUser?.Id ?? "Unknown", ObfuscatedRoomCode, session.LobbyRegistration.Uri);
                NavigationService.ToHome();
                return;
            }

            if (roomCode.Trim() != ObfuscatedRoomCode)
            {
                Logger.LogError("User [{userId}] attempted to enter room [{code}] but their session registration uri [{uri}] does not match.",
                    UserService.CurrentUser?.Id ?? "Unknown", ObfuscatedRoomCode, session.LobbyRegistration.Uri);
                NavigationService.ToHome();
                return;
            }

            GameState = (DrawnToDressGameState)session.LobbyRegistration.State;

            if (GameState.IsDisposed)
            {
                NavigationService.ToHome();
                return;
            }

            GameState.OnStateDisposed += HandleStateDisposed;
            _stateSubscription = GameState.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));

            _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            _ = StartTimerAsync();

            await base.OnInitializedAsync();
        }

        private async Task StartTimerAsync()
        {
            try
            {
                while (await _timer!.WaitForNextTickAsync(ComponentDetached))
                {
                    try
                    {
                        await InvokeAsync(() =>
                        {
                            // Only the host drives FSM ticks to avoid concurrent transitions.
                            if (GameState?.Context is not null && IsHost())
                                GameEngine.Tick(GameState.Context, DateTimeOffset.UtcNow);
                            StateHasChanged();
                        });
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error during timer tick.");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Timer loop terminated unexpectedly.");
            }
        }

        private bool IsHost() => UserService.CurrentUser?.Id == GameState?.Host?.Id;

        protected override void OnAfterRender(bool firstRender)
        {
            if (GameState is not null && GameState.KickedPlayers.Contains(UserService.CurrentUser))
            {
                GameSessionService.LeaveCurrentSession(navigateHome: true);
            }

            base.OnAfterRender(firstRender);
        }

        private void HandleStateDisposed()
        {
            InvokeAsync(() =>
            {
                GameSessionService.LeaveCurrentSession(navigateHome: false);
                NavigationService.ToHome();
            });
        }

        public override void Dispose()
        {
            base.Dispose();
            _timer?.Dispose();
            if (GameState is not null)
            {
                GameState.OnStateDisposed -= HandleStateDisposed;
            }
            _stateSubscription?.Dispose();
        }

        private static bool TryExtractObfuscatedRoomCode(string uri, [NotNullWhen(true)] out string? obfuscatedRoomCode)
        {
            obfuscatedRoomCode = null;
            var split = uri.Trim().Trim('/').Split('/');
            if (split.Length <= 0) return false;
            obfuscatedRoomCode = split[^1];
            return true;
        }

        protected DrawnToDressGameState GameState { get; set; } = default!;
    }
}
