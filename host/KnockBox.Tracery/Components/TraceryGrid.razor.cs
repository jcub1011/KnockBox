using System.Globalization;
using System.Text;
using KnockBox.Tracery.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace KnockBox.Tracery.Components
{
    /// <summary>
    /// Renders the player's copy of the shared grid and captures a traced word by either
    /// tap-adjacent selection or smooth pointer/touch drag. Both gestures mutate one ordered
    /// <see cref="_path"/> of cell ids (Word-Hunt feel: a step must be 8-way adjacent and
    /// unvisited; returning to the previous cell pops the last one) and finish through
    /// <see cref="OnPathSubmitted"/>. Legality shown here is only a preview — the engine's
    /// <c>TracerySolver.ValidateTrace</c> is the authority.
    /// </summary>
    public partial class TraceryGrid : IDisposable
    {
        [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] protected ILogger<TraceryGrid> Logger { get; set; } = default!;

        /// <summary>The grid to render and trace over.</summary>
        [Parameter, EditorRequired] public Grid? Grid { get; set; }

        /// <summary>When false (round not active / player finished), input is ignored.</summary>
        [Parameter] public bool IsActive { get; set; }

        /// <summary>Set briefly by the host to shake the grid after a rejected trace.</summary>
        [Parameter] public bool Invalid { get; set; }

        /// <summary>Raised with the completed cell-id path when a trace is submitted.</summary>
        [Parameter] public EventCallback<IReadOnlyList<int>> OnPathSubmitted { get; set; }

        private readonly List<int> _path = new();
        private ElementReference _gridEl;
        private IJSObjectReference? _module;
        private DotNetObjectReference<TraceryGrid>? _dotNetRef;
        private Grid? _renderedGrid;

        protected override void OnParametersSet()
        {
            // A new round swaps in a fresh Grid instance — drop any half-built trace so it
            // can't carry stale cell ids onto the new board.
            if (!ReferenceEquals(_renderedGrid, Grid))
            {
                _renderedGrid = Grid;
                _path.Clear();
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
                catch (Exception ex)
                {
                    // Drag won't work without the module, but tap (pure Blazor) still does.
                    Logger.LogWarning(ex, "Tracery drag interop initialization failed.");
                }
            }
            await base.OnAfterRenderAsync(firstRender);
        }

        // ── Drag (JS-driven) ────────────────────────────────────────────────

        // The JS module dispatches these from a pointer event; InvokeAsync marshals the path
        // mutation + render onto the Blazor synchronization context (the SpardleRoom precedent).

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
