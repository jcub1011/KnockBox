using System.Globalization;
using System.Text;
using KnockBox.Tracery.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KnockBox.Tracery.Client.Components
{
    /// <summary>
    /// Renders the player's copy of the shared grid and captures a traced word by either
    /// tap-adjacent selection or smooth pointer/touch drag. Both gestures mutate one ordered
    /// <see cref="_path"/> of cell ids (a step must be 8-way adjacent and unvisited; returning to
    /// the previous cell pops the last one) and finish through <see cref="OnPathSubmitted"/>.
    /// Legality shown here is only a preview — the engine's <c>TracerySolver.ValidateTrace</c> is the
    /// authority. The drag JS dispatches from pointer events (user gestures), so its
    /// <c>invokeMethodAsync</c> callbacks are safe on WASM (the heap-lock footgun only bites events
    /// Blazor fires mid-render).
    /// </summary>
    public partial class TraceryGrid : IDisposable
    {
        [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;

        /// <summary>The grid to render and trace over.</summary>
        [Parameter, EditorRequired] public Grid? Grid { get; set; }

        /// <summary>When false (round not active / player finished), input is ignored.</summary>
        [Parameter] public bool IsActive { get; set; }

        /// <summary>Set briefly by the parent to shake the grid after a rejected trace.</summary>
        [Parameter] public bool Invalid { get; set; }

        /// <summary>Raised with the completed cell-id path when a trace is submitted.</summary>
        [Parameter] public EventCallback<IReadOnlyList<int>> OnPathSubmitted { get; set; }

        /// <summary>Raised when an in-progress drag is interrupted by the OS and discarded (not submitted).</summary>
        [Parameter] public EventCallback OnTraceCancelled { get; set; }

        private readonly List<int> _path = new();
        private int _rotationQuarterTurns; // 0..3; * 90 = clockwise degrees of the player's view
        private ElementReference _gridEl;
        private IJSObjectReference? _module;
        private DotNetObjectReference<TraceryGrid>? _dotNetRef;
        private string? _renderedLetters;

        protected override void OnParametersSet()
        {
            // Detect a new board by VALUE (its letters), not by Grid reference: in the WASM model
            // every projection deserializes a fresh Grid instance for the SAME board, so a
            // reference check would wipe a half-built trace on every projection (e.g. an opponent
            // banking, or the host rail updating) that arrives mid-drag. Only a genuinely different
            // board (a new round) changes the letters.
            if (!string.Equals(_renderedLetters, Grid?.Letters, StringComparison.Ordinal))
            {
                _renderedLetters = Grid?.Letters;
                _path.Clear();
                _rotationQuarterTurns = 0; // each new round starts at the default orientation
            }

            // The round ended (timer/complete) — clear the preview so the locked grid is clean.
            if (!IsActive && _path.Count > 0)
                _path.Clear();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                _dotNetRef = DotNetObjectReference.Create(this);
                try
                {
                    _module = await JSRuntime.InvokeAsync<IJSObjectReference>(
                        "import", "./_content/KnockBox.Tracery/tracery-trace.js");
                    await _module.InvokeVoidAsync("init", _gridEl, _dotNetRef);
                }
                catch
                {
                    // Drag won't work without the module, but tap (pure Blazor) still does.
                }
            }
            await base.OnAfterRenderAsync(firstRender);
        }

        // ── Drag (JS-driven) ────────────────────────────────────────────────

        /// <summary>Pointer went down on a cell — begin a fresh trace there.</summary>
        [JSInvokable]
        public Task OnDragStart(int cellId) => InvokeAsync(() =>
        {
            if (!IsActive) return;
            StartPath(cellId);
            StateHasChanged();
        });

        /// <summary>Pointer dragged into a different cell — extend (or backtrack) the trace.</summary>
        [JSInvokable]
        public Task OnDragEnter(int cellId) => InvokeAsync(() =>
        {
            if (!IsActive) return;
            ExtendPath(cellId);
            StateHasChanged();
        });

        /// <summary>Pointer released — submit the trace (unless it never left the start cell).</summary>
        [JSInvokable]
        public Task OnDragEnd() => InvokeAsync(async () =>
        {
            if (!IsActive) { _path.Clear(); StateHasChanged(); return; }
            if (_path.Count >= 2)
            {
                await SubmitPath();
            }
            else
            {
                // A tap-like down/up that never crossed into a second cell — nothing to submit.
                _path.Clear();
                StateHasChanged();
            }
        });

        /// <summary>
        /// Drag interrupted by the OS (a second finger, a system gesture, an incoming notification).
        /// The gesture wasn't deliberately completed, so the half-built trace is discarded rather than
        /// submitted, and the parent is notified so it can hint why the in-progress word disappeared.
        /// </summary>
        [JSInvokable]
        public Task OnDragCancel() => InvokeAsync(async () =>
        {
            bool hadTrace = _path.Count >= 2;
            _path.Clear();
            StateHasChanged();
            if (hadTrace && OnTraceCancelled.HasDelegate)
                await OnTraceCancelled.InvokeAsync();
        });

        // ── Tap (Blazor-driven) ─────────────────────────────────────────────

        private async Task OnCellTap(int cellId)
        {
            if (!IsActive) return;

            // Tapping the current end cell finishes the word (≥2 cells) or deselects it (1 cell).
            if (_path.Count > 0 && _path[^1] == cellId)
            {
                if (_path.Count >= 2) await SubmitPath();
                else ClearPath();
                return;
            }

            if (_path.Count == 0) StartPath(cellId);
            else ExtendPath(cellId);
            StateHasChanged();
        }

        // ── View rotation (client-only) ─────────────────────────────────────

        /// <summary>Clockwise degrees the player has rotated their own view of the board.</summary>
        private int RotationDegrees => _rotationQuarterTurns * 90;

        /// <summary>
        /// Turn the player's view 90° clockwise. Purely visual: cell ids, the trace path,
        /// adjacency, and scoring are untouched, so no new words can be formed.
        /// </summary>
        private void RotateGrid() => _rotationQuarterTurns = (_rotationQuarterTurns + 1) % 4;

        // ── Shared path model ───────────────────────────────────────────────

        private void StartPath(int cellId)
        {
            _path.Clear();
            _path.Add(cellId);
        }

        private void ExtendPath(int cellId)
        {
            if (Grid is null) return;
            if (_path.Count == 0) { _path.Add(cellId); return; }

            int last = _path[^1];
            if (cellId == last) return;                                   // same cell — ignore
            if (_path.Count >= 2 && cellId == _path[^2])                  // back to previous — pop
            {
                _path.RemoveAt(_path.Count - 1);
                return;
            }
            if (_path.Contains(cellId)) return;                          // already used — can't revisit
            if (Grid.AreAdjacent(last, cellId)) _path.Add(cellId);        // legal step — append
            // else: not adjacent — ignore (illegal preview)
        }

        /// <summary>
        /// Submit button handler — ships the current trace for players who built it by tapping
        /// and don't know the tap-the-last-tile / double-tap shortcut. The engine validates length.
        /// </summary>
        private async Task SubmitWord()
        {
            if (!IsActive || _path.Count == 0) return;
            await SubmitPath();
        }

        private async Task SubmitPath()
        {
            var path = _path.ToArray();
            _path.Clear();
            StateHasChanged();
            if (OnPathSubmitted.HasDelegate)
                await OnPathSubmitted.InvokeAsync(path);
        }

        private void ClearPath()
        {
            _path.Clear();
            StateHasChanged();
        }

        // ── Rendering helpers ───────────────────────────────────────────────

        // Centers of the path cells in viewBox units (each cell is 1×1), for the connecting line.
        private string LinePoints()
        {
            if (Grid is null) return string.Empty;
            var sb = new StringBuilder();
            foreach (var cellId in _path)
            {
                var (r, c) = Grid.FromCellId(cellId);
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(CultureInfo.InvariantCulture, $"{c + 0.5:0.###},{r + 0.5:0.###}");
            }
            return sb.ToString();
        }

        private string CurrentWord()
        {
            if (Grid is null || _path.Count == 0) return string.Empty;
            var sb = new StringBuilder(_path.Count);
            foreach (var cellId in _path)
                sb.Append(char.ToUpperInvariant(Grid[cellId]));
            return sb.ToString();
        }

        public void Dispose()
        {
            if (_module is not null)
            {
                var module = _module;
                _module = null;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await module.InvokeVoidAsync("dispose");
                        await module.DisposeAsync();
                    }
                    catch { /* circuit may already be gone */ }
                });
            }
            _dotNetRef?.Dispose();
        }
    }
}
