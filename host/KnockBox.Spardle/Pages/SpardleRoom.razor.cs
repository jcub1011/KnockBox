using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.Navigation;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Spardle.Pages;

public partial class SpardleRoom : DisposableComponent
{
    [Inject] protected SpardleEngine GameEngine { get; set; } = default!;
    [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;
    [Inject] protected INavigationService NavigationService { get; set; } = default!;
    [Inject] protected IUserService UserService { get; set; } = default!;
    [Inject] protected ILogger<SpardleRoom> Logger { get; set; } = default!;

    [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

    private IDisposable? _stateSubscription;
    protected SpardleState GameState { get; set; } = default!;
    protected string RoomCode { get; set; } = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        if (UserService.CurrentUser is null)
            await UserService.InitializeCurrentUserAsync(ComponentDetached);

        if (!GameSessionService.TryGetCurrentSession(out var session))
        {
            NavigationService.ToHome();
            return;
        }

        if (!TryExtractObfuscatedRoomCode(session.LobbyRegistration.Uri, out var roomCode) || roomCode.Trim() != ObfuscatedRoomCode)
        {
            NavigationService.ToHome();
            return;
        }

        GameState = (SpardleState)session.LobbyRegistration.State;

        if (GameState.IsDisposed)
        {
            NavigationService.ToHome();
            return;
        }

        GameState.OnStateDisposed += HandleStateDisposed;
        RoomCode = session.LobbyRegistration.Code;
        _stateSubscription = GameState.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));

        await base.OnInitializedAsync();
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (GameState?.IsKicked(UserService.CurrentUser!) == true)
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
        if (GameState is not null) GameState.OnStateDisposed -= HandleStateDisposed;
        _stateSubscription?.Dispose();
        base.Dispose();
    }

    private static bool TryExtractObfuscatedRoomCode(string uri, [NotNullWhen(true)] out string? obfuscatedRoomCode)
    {
        obfuscatedRoomCode = null;
        var split = uri.Trim().Trim('/').Split('/');
        if (split.Length <= 0) return false;
        obfuscatedRoomCode = split[^1];
        return true;
    }
}