using KnockBox.Core.Components.Shared;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Navigation;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Spardle.Components;
using KnockBox.Spardle.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Spardle.Pages;

public partial class SpardleRoom : DisposableComponent
{
    [Inject] protected SpardleEngine GameEngine { get; set; } = default!;
    [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;
    [Inject] protected INavigationService NavigationService { get; set; } = default!;
    [Inject] protected IUserService UserService { get; set; } = default!;
    [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] protected ILogger<SpardleRoom> Logger { get; set; } = default!;

    [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

    private IDisposable? _stateSubscription;
    private IJSObjectReference? _keyboardModule;
    private IJSObjectReference? _storageModule;
    private DotNetObjectReference<SpardleRoom>? _dotNetRef;
    private CancellationTokenSource? _toastCts;
    private CancellationTokenSource? _shakeCts;
    private GamePhase _previousPhase = GamePhase.Lobby;

    protected SpardleState GameState { get; set; } = default!;
    protected string RoomCode { get; set; } = string.Empty;
    protected bool HighContrast { get; set; }

    private string _currentGuess = string.Empty;
    private string? _toastMessage;
    private SpardleToast.ToastTone _toastTone = SpardleToast.ToastTone.Danger;
    private bool _invalidGuess;

    protected override async Task OnInitializedAsync()
    {
        if (UserService.CurrentUser is null)
            await UserService.InitializeCurrentUserAsync(ComponentDetached);

        if (!GameSessionService.TryGetCurrentSession(out var session))
        {
            NavigationService.ToHome();
            return;
        }

        if (!TryExtractObfuscatedRoomCode(session.LobbyRegistration.Uri, out var roomCode)
            || roomCode.Trim() != ObfuscatedRoomCode)
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
        _previousPhase = GameState.Phase;
        _stateSubscription = GameState.StateChangedEventManager.Subscribe(async () =>
        {
            if (GameState.Phase != _previousPhase)
            {
                _previousPhase = GameState.Phase;
                _currentGuess = string.Empty;
            }
            await InvokeAsync(StateHasChanged);
        });

        await base.OnInitializedAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            try
            {
                _keyboardModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./_content/KnockBox.Spardle/js/spardle-keyboard.js");
                await _keyboardModule.InvokeVoidAsync("register", _dotNetRef);

                _storageModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./_content/KnockBox.Spardle/js/spardle-storage.js");
                HighContrast = await _storageModule.InvokeAsync<bool>("loadHc");
                StateHasChanged();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Spardle JS interop initialization failed.");
            }
        }

        if (GameState?.IsKicked(UserService.CurrentUser!) == true)
        {
            GameSessionService.LeaveCurrentSession(navigateHome: true);
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    [JSInvokable]
    public Task OnPhysicalKey(string key) => InvokeAsync(async () =>
    {
        await HandleKeyPress(key);
    });

    private async Task HandleKeyPress(string key)
    {
        if (GameState is null || UserService.CurrentUser is null) return;
        if (GameState.Phase != GamePhase.Playing) return;

        var playerState = GameState.GetOrCreatePlayerState(UserService.CurrentUser.Id);
        if (playerState.HasFinishedRound) return;

        int wordLen = GameState.TargetWord.Length;

        if (key == "ENTER")
        {
            if (_currentGuess.Length != wordLen)
            {
                await Task.WhenAll(
                    ShowToast($"NEEDS {wordLen} LETTERS", SpardleToast.ToastTone.Warn),
                    TriggerInvalidShake());
                return;
            }
            var result = GameEngine.SubmitGuess(GameState, UserService.CurrentUser, _currentGuess);
            if (result.IsSuccess)
            {
                _currentGuess = string.Empty;
            }
            else if (result.TryGetFailure(out var failure))
            {
                await Task.WhenAll(
                    ShowToast(failure.PublicMessage.ToUpperInvariant(), SpardleToast.ToastTone.Danger),
                    TriggerInvalidShake());
            }
            StateHasChanged();
        }
        else if (key == "BACKSPACE")
        {
            if (_currentGuess.Length > 0)
            {
                _currentGuess = _currentGuess[..^1];
                StateHasChanged();
            }
        }
        else if (key.Length == 1 && char.IsLetter(key[0]) && _currentGuess.Length < wordLen)
        {
            _currentGuess += char.ToLowerInvariant(key[0]);
            StateHasChanged();
        }
    }

    private async Task ShowToast(string message, SpardleToast.ToastTone tone)
    {
        _toastCts?.Cancel();
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;
        _toastMessage = message;
        _toastTone = tone;
        StateHasChanged();
        try
        {
            await Task.Delay(1800, token);
            if (!token.IsCancellationRequested)
            {
                _toastMessage = null;
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { /* superseded */ }
    }

    private async Task TriggerInvalidShake()
    {
        _shakeCts?.Cancel();
        _shakeCts = new CancellationTokenSource();
        var token = _shakeCts.Token;
        _invalidGuess = true;
        StateHasChanged();
        try
        {
            await Task.Delay(500, token);
            if (!token.IsCancellationRequested)
            {
                _invalidGuess = false;
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { /* superseded */ }
    }

    private async Task ToggleContrast()
    {
        HighContrast = !HighContrast;
        if (_storageModule is not null)
        {
            try { await _storageModule.InvokeVoidAsync("saveHc", HighContrast); }
            catch { /* ignore */ }
        }
        StateHasChanged();
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
        _toastCts?.Cancel();
        _shakeCts?.Cancel();
        if (GameState is not null) GameState.OnStateDisposed -= HandleStateDisposed;
        _stateSubscription?.Dispose();

        if (_keyboardModule is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _keyboardModule.InvokeVoidAsync("unregister");
                    await _keyboardModule.DisposeAsync();
                }
                catch { /* ignore */ }
            });
        }
        if (_storageModule is not null)
        {
            _ = _storageModule.DisposeAsync();
        }
        _dotNetRef?.Dispose();
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
