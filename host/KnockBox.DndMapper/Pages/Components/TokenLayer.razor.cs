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

        /// <summary>
        /// When <c>true</c>, the token layer becomes non-interactive (pointer-events:none on
        /// its root group) so clicks pass through to the underlying SVG. Used by MapCanvas
        /// while a host-only canvas tool (fog paint/erase, focus-box) is active.
        /// </summary>
        [Parameter] public bool InteractionsDisabled { get; set; }

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
                : TokenVisibilityFilter.VisibleTokensFor(Map.Tokens, Map, IsHost);

        private List<TokenStack> Stacks => TokenStackGrouper.Group(VisibleTokens);

        // Token id of the combatant whose turn is currently active. Returns
        // null unless combat is in the Active phase and the current turn
        // index points at a combatant carrying a non-empty TokenId. Drives
        // the .dndm-token--active glow class on the matching token's root <g>.
        private Guid? ActiveTurnTokenId
        {
            get
            {
                var combat = State.ActiveCombat;
                if (combat is null || combat.Phase != CombatPhase.Active) return null;
                var turn = combat.TurnOrder;
                if (turn.Count == 0) return null;
                var idx = combat.CurrentTurnIndex;
                if (idx < 0 || idx >= turn.Count) return null;
                var tokenId = turn[idx].TokenId;
                return tokenId == Guid.Empty ? null : tokenId;
            }
        }

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
                // Force the visual back to the authoritative coords. Required when
                // the snapped drop lands on the same cell the drag started in —
                // Blazor's transform-attribute diff is a no-op and would leave the
                // JS-applied mid-cell transform in place.
                if (_jsModule is not null)
                {
                    try
                    {
                        await _jsModule.InvokeVoidAsync("reconcileToken", SvgId, tokenIdStr, sx, sy);
                    }
                    catch (JSDisconnectedException) { /* circuit teardown */ }
                }

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
        {
            var classes = token.Hidden ? "dndm-token dndm-token--hidden" : "dndm-token";
            if (ActiveTurnTokenId is { } id && id == token.Id)
                classes += " dndm-token--active";
            return classes;
        }

        private string FillFor(Token token)
        {
            var color = token.ResolveColor(State.Sheets);
            if (string.IsNullOrEmpty(color)) return "#444";
            // Use the resolved color directly as the fill — distinct from the
            // border because we render the border at the same color but with
            // a slight opacity bump in CSS.
            return color;
        }

        private string TextFillFor(Token token)
            => TokenTextContrast.TextFillFor(token.ResolveColor(State.Sheets));

        // The token's effective stroke/border color — used inline in the
        // SVG render tree. Sheet color wins when the token is linked to a
        // colored sheet; falls back to Token.Color then to the neutral gray.
        private string StrokeFor(Token token)
        {
            var color = token.ResolveColor(State.Sheets);
            return string.IsNullOrEmpty(color) ? "#888" : color;
        }

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
