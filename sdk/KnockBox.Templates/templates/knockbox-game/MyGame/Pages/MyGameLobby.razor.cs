// -----------------------------------------------------------------------------
// Code-behind for MyGameLobby.razor.
//
// Declared as a partial of the Razor-generated class so that markup lives in
// MyGameLobby.razor and behavior lives here. This is the conventional layout
// for non-trivial Blazor components in the KnockBox codebase.
// -----------------------------------------------------------------------------

using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.Navigation;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace MyGame.Pages;

/// <summary>
/// Lobby / in-progress page for the plugin. Marked <c>partial</c> so the
/// Razor SDK's generated class can attach this behavior to the markup in
/// <c>MyGameLobby.razor</c>.
/// </summary>
public partial class MyGameLobby : DisposableComponent
{
    // ---- Injected services ---------------------------------------------------
    // Concrete engine — use this to invoke game commands (StartAsync, custom
    // actions). Registered as a singleton via AddGameEngine<T>() in MyGameModule.
    [Inject] protected MyGameGameEngine GameEngine { get; set; } = default!;

    // Active game session for this circuit (scoped per Blazor circuit). Survives
    // 1-minute disconnect grace periods via the platform's SessionServiceProvider.
    [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;

    // Typed navigation (ToHome, ToGame, etc.) — prefer over NavigationManager.
    [Inject] protected INavigationService NavigationService { get; set; } = default!;

    // Current user identity for this circuit (scoped).
    [Inject] protected IUserService UserService { get; set; } = default!;

    [Inject] protected ILogger<MyGameLobby> Logger { get; set; } = default!;

    // URL parameter — the platform generates obfuscated codes in LobbyService
    // and embeds them into the lobby URI. Used below to validate the session
    // matches this URL.
    [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

    private MyGameGameState? GameState { get; set; }

    // Must be disposed in Dispose() — otherwise the state holds a reference to
    // this component and leaks the circuit after navigation. Non-negotiable.
    private IDisposable? _stateSubscription;

    private bool IsHost => GameState?.Host.Id == UserService.CurrentUser?.Id;
    private bool CanStart => GameState is not null && GameState.Players.Count > 0;

    protected override async Task OnInitializedAsync()
    {
        // 1. Ensure we have a user identity (name + id). On a fresh circuit the
        //    UserService may not have initialized yet; pass our cancellation
        //    token so initialization cancels cleanly if the component detaches.
        if (UserService.CurrentUser is null)
            await UserService.InitializeCurrentUserAsync(ComponentDetached);

        // 2. Bail to home if there's no active session — this usually means the
        //    user refreshed after the session's disconnect grace period expired,
        //    pasted a URL from somewhere, or the lobby was closed.
        if (!GameSessionService.TryGetCurrentSession(out var session))
        {
            NavigationService.ToHome();
            return;
        }

        // 3. Defensive type-check. If the session exists but points at the wrong
        //    game's state type, something has gone badly wrong upstream.
        if (session.LobbyRegistration.State is not MyGameGameState gameState)
        {
            Logger.LogError("Game state is not the expected type.");
            NavigationService.ToHome();
            return;
        }

        GameState = gameState;

        // Dispose-listener: when the lobby ends (host leaves / game over /
        // explicit close), leave the session and go home.
        GameState.OnStateDisposed += HandleStateDisposed;

        // Re-render this component whenever the state mutates. InvokeAsync
        // marshals the StateHasChanged call back onto the Blazor render
        // dispatcher, which is mandatory — the notification can fire from any
        // thread (e.g., a background tick handler).
        _stateSubscription = GameState.StateChangedEventManager.Subscribe(
            async () => await InvokeAsync(StateHasChanged));

        await base.OnInitializedAsync();
    }

    private async Task StartGame()
    {
        if (GameState is null || UserService.CurrentUser is null) return;

        // The engine returns a Result — in a real game you'd branch on
        // result.TryGetFailure(...) to surface a toast or inline error.
        await GameEngine.StartAsync(UserService.CurrentUser, GameState, ComponentDetached);
    }

    private void HandleStateDisposed()
    {
        // The state has already torn itself down; don't ask the session
        // service to navigate — we handle the redirect ourselves.
        GameSessionService.LeaveCurrentSession(navigateHome: false);
        NavigationService.ToHome();
    }

    public override void Dispose()
    {
        if (GameState is not null)
            GameState.OnStateDisposed -= HandleStateDisposed;

        // Dispose the subscription BEFORE base.Dispose(). If we don't, the
        // StateChangedEventManager keeps a live Func<ValueTask> reference to
        // this component's closure and the circuit leaks.
        _stateSubscription?.Dispose();
        base.Dispose();
    }
}
