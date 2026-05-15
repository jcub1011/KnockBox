using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class TokenLayer : DisposableComponent, IAsyncDisposable
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public Map? Map { get; set; }
        [Parameter] public bool IsHost { get; set; }
        [Parameter] public string CurrentUserId { get; set; } = string.Empty;
        [Parameter, EditorRequired] public string SvgId { get; set; } = string.Empty;

        [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] protected DndMapperGameEngine GameEngine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected ILogger<TokenLayer> Logger { get; set; } = default!;

        private IJSObjectReference? _jsModule;
        private DotNetObjectReference<TokenLayer>? _dotNetRef;
        private IDisposable? _stateSub;
        private bool _initialized;
        private int _lastBoundsWidth;
        private int _lastBoundsHeight;
        private TokenCellKey? _expandedCell;

        private IEnumerable<Token> VisibleTokens =>
            Map is null
                ? []
                : TokenVisibilityFilter.VisibleTokensFor(Map.Tokens, IsHost);

        private List<TokenStack> Stacks => TokenStackGrouper.Group(VisibleTokens);

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(OnStateChangedAsync);
            base.OnInitialized();
        }

        private async ValueTask OnStateChangedAsync()
        {
            // Re-sync happens in OnAfterRenderAsync once Blazor has rebound parameters
            // (the Map may have just been swapped). We only request a re-render here.
            await InvokeAsync(StateHasChanged);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                try
                {
                    _dotNetRef = DotNetObjectReference.Create(this);
                    _jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                        "import", "./_content/KnockBox.DndMapper/js/dndMapperTokenDrag.js");

                    _lastBoundsWidth = Map?.Grid.WidthCells ?? 0;
                    _lastBoundsHeight = Map?.Grid.HeightCells ?? 0;
                    await _jsModule.InvokeVoidAsync(
                        "initialize", SvgId, _dotNetRef, BuildTokenJsList(), _lastBoundsWidth, _lastBoundsHeight);
                    _initialized = true;
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to initialize token drag JS for svg [{SvgId}].", SvgId);
                }
            }
            else if (_jsModule is not null && _initialized)
            {
                try
                {
                    int w = Map?.Grid.WidthCells ?? 0;
                    int h = Map?.Grid.HeightCells ?? 0;
                    if (w != _lastBoundsWidth || h != _lastBoundsHeight)
                    {
                        _lastBoundsWidth = w;
                        _lastBoundsHeight = h;
                        await _jsModule.InvokeVoidAsync("setBounds", SvgId, w, h);
                    }
                    await _jsModule.InvokeVoidAsync("setMovableTokens", SvgId, BuildTokenJsList());
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to re-sync token JS state for svg [{SvgId}].", SvgId);
                }
            }
            await base.OnAfterRenderAsync(firstRender);
        }

        private object[] BuildTokenJsList()
        {
            if (Map is null) return [];
            var list = new List<object>(Map.Tokens.Count);
            foreach (var t in VisibleTokens)
            {
                bool isOwner = t.OwnerUserId is not null && t.OwnerUserId == CurrentUserId;
                bool isParticipant = IsHost
                    || State.Players.Any(p => p.User.Id == CurrentUserId);
                bool movable = TokenMovabilityResolver.CanMove(
                    IsHost, isOwner, isParticipant, State.Settings.TokenMovement);
                list.Add(new
                {
                    tokenId = t.Id.ToString(),
                    x = t.X,
                    y = t.Y,
                    movable,
                });
            }
            return list.ToArray();
        }

        private void OpenStack(TokenCellKey cell) => _expandedCell = cell;

        private void CloseStack() => _expandedCell = null;

        [JSInvokable]
        public async Task OnTokenDragEnd(string tokenIdStr, double x, double y)
        {
            if (!Guid.TryParse(tokenIdStr, out var tokenId)) return;
            if (Map is null || UserService.CurrentUser is null) return;

            var (sx, sy) = SnapToGridHelper.Snap(x, y, Map.Grid);

            var result = GameEngine.MoveTokenAsync(State, UserService.CurrentUser, tokenId, sx, sy);
            if (result.IsSuccess)
            {
                // Close any open stack popover only on a successful move — the
                // chip layout no longer reflects the underlying stack composition.
                // On failure we keep the popover open so the user can retry.
                _expandedCell = null;
            }
            else if (_jsModule is not null)
            {
                try
                {
                    await _jsModule.InvokeVoidAsync("revertToken", SvgId, tokenIdStr);
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }

                if (result.TryGetFailure(out var err))
                {
                    Logger.LogInformation("Token move rejected for token [{TokenId}]: {Error}", tokenId, err);
                }
            }
        }

        private static string Fmt(double value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);

        private string TokenCssClass(Token token)
            => token.Hidden ? "dndm-token dndm-token--hidden" : "dndm-token";

        private static string FillFor(Token token)
        {
            if (string.IsNullOrEmpty(token.Color)) return "#444";
            // Use the token's color directly as the fill — distinct from the
            // border because we render the border at the same color but with
            // a slight opacity bump in CSS.
            return token.Color;
        }

        private static string TextFillFor(Token token)
            => TokenTextContrast.TextFillFor(token.Color);

        public async ValueTask DisposeAsync()
        {
            _stateSub?.Dispose();
            if (_jsModule is not null)
            {
                try { await _jsModule.InvokeVoidAsync("dispose", SvgId); }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception) { /* ignore */ }

                try { await _jsModule.DisposeAsync(); }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception) { /* ignore */ }
            }
            _dotNetRef?.Dispose();
            Dispose();
        }
    }
}
