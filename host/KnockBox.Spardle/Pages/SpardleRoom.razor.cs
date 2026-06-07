using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.PlayLog;
using KnockBox.Spardle.Components;
using KnockBox.Spardle.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace KnockBox.Spardle.Pages;

public partial class SpardleRoom : LobbyPageBase<SpardleState>
{
    [Inject] protected SpardleEngine GameEngine { get; set; } = default!;
    [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;

    private IJSObjectReference? _keyboardModule;
    private IJSObjectReference? _storageModule;
    private DotNetObjectReference<SpardleRoom>? _dotNetRef;
    private CancellationTokenSource? _toastCts;
    private CancellationTokenSource? _shakeCts;
    private GamePhase _previousPhase = GamePhase.Lobby;

    protected bool HighContrast { get; set; }

    protected bool IsHostObserver =>
        GameState is not null
        && !GameState.HostIsParticipant
        && UserService.CurrentUser?.Id == GameState.Host.Id;

    private string _currentGuess = string.Empty;
    private string? _toastMessage;
    private SpardleToast.ToastTone _toastTone = SpardleToast.ToastTone.Danger;
    private bool _invalidGuess;

    protected override Task OnLobbyInitializedAsync()
    {
        _previousPhase = GameState.Phase;
        return Task.CompletedTask;
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

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override async ValueTask OnStateChangedAsync()
    {
        if (GameState.Phase != _previousPhase)
        {
            _previousPhase = GameState.Phase;
            _currentGuess = string.Empty;
        }
        await base.OnStateChangedAsync();
    }

    /// <summary>
    /// Records a match-level play-log entry once the game reaches
    /// <see cref="GamePhase.GameOver"/>. Returns <c>null</c> while the match is
    /// still in progress so the base logs the first non-null result exactly once.
    /// </summary>
    protected override GameLog? BuildEndOfGamePlayLog()
    {
        if (GameState.Phase != GamePhase.GameOver)
            return null;

        return GameLog.Create(
            "spardle",
            SpardlePlayLogMetadata.Build(GameState, UserService.CurrentUser?.Id));
    }

    [JSInvokable]
    public Task OnPhysicalKey(string key) => InvokeAsync(async () =>
    {
        await HandleKeyPress(key);
    });

    private async Task HandleKeyPress(string key)
    {
        if (GameState is null || UserService.CurrentUser is null) return;
        if (IsHostObserver) return;
        if (GameState.Phase != GamePhase.Playing) return;

        if (!GameState.TryGetPlayerState(UserService.CurrentUser.Id, out var playerState)) return;
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

    private async Task HandleGiveUp()
    {
        if (GameState is null || UserService.CurrentUser is null) return;
        if (IsHostObserver) return;
        if (GameState.Phase != GamePhase.Playing) return;

        if (!GameState.TryGetPlayerState(UserService.CurrentUser.Id, out var playerState)) return;
        if (playerState.HasFinishedRound) return;

        var result = GameEngine.GiveUp(GameState, UserService.CurrentUser);
        if (!result.IsSuccess && result.TryGetFailure(out var failure))
        {
            await ShowToast(failure.PublicMessage.ToUpperInvariant(), SpardleToast.ToastTone.Danger);
        }
        StateHasChanged();
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

    protected override void OnLobbyDisposing()
    {
        _toastCts?.Cancel();
        _shakeCts?.Cancel();

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
    }
}
