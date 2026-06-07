using KnockBox.Core.Components.Shared;
using KnockBox.Core.Plugins;
using KnockBox.Core.Primitives.Exceptions;
using KnockBox.Core.Services.State.PlayLog;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace KnockBox.Platform.Components.Pages.Home
{
    /// <summary>
    /// Home-page rail showing the user's recent <see cref="IPlayLogService"/>
    /// history, newest first. Each entry shows the game's display name, whether
    /// the user hosted or joined, a relative timestamp, and the game-supplied
    /// metadata as a key/value table (capped, with a per-entry "show more" toggle).
    /// </summary>
    /// <remarks>
    /// <see cref="IPlayLogService"/> reaches the browser via JS interop, which is
    /// unavailable during prerendering — so the history is loaded in
    /// <see cref="OnAfterRenderAsync"/> (first render only), never in
    /// <c>OnInitialized*</c>.
    /// </remarks>
    public partial class PlayLogPanel : DisposableComponent
    {
        /// <summary>Metadata rows shown before the "show more" toggle appears.</summary>
        private const int MetadataPreviewCount = 5;

        [Inject] IPlayLogService PlayLog { get; set; } = default!;
        [Inject] IEnumerable<IGameModule> GameModules { get; set; } = default!;
        [Inject] ILogger<PlayLogPanel> Logger { get; set; } = default!;

        private IReadOnlyList<GameLog> _logs = Array.Empty<GameLog>();
        private readonly HashSet<int> _expanded = [];
        private IReadOnlyDictionary<string, string>? _gameNames;
        private IReadOnlyDictionary<string, (string? Background, string? Font)>? _gameColors;
        private bool _loaded;
        private bool _loadFailed;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;

            try
            {
                var result = await PlayLog.GetLogsAsync(ComponentDetached);
                if (result.IsCanceled) return;

                if (result.TryGetSuccess(out var logs))
                {
                    _logs = logs;
                }
                else
                {
                    _loadFailed = true;
                    result.TryGetFailure(out var error);
                    Logger.LogWarning("Could not load play log for home page: {error}", error.InternalMessage);
                }
            }
            catch (Exception ex)
            {
                if (ex.TryGetCancellationException(out _)) return;
                _loadFailed = true;
                Logger.LogError(ex, "Error loading play log for home page.");
            }
            finally
            {
                _loaded = true;
            }

            await InvokeAsync(StateHasChanged);
        }

        /// <summary>
        /// Maps a stored <see cref="GameLog.GameIdentifier"/> (a plugin route id)
        /// to its display name, falling back to the raw identifier if the game is
        /// no longer installed.
        /// </summary>
        private string GameName(string routeIdentifier)
        {
            _gameNames ??= BuildGameNameMap();
            return _gameNames.TryGetValue(routeIdentifier, out var name) ? name : routeIdentifier;
        }

        private IReadOnlyDictionary<string, string> BuildGameNameMap()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var module in GameModules)
                map[module.Manifest.RouteIdentifier] = module.Manifest.Name;
            return map;
        }

        /// <summary>
        /// Inline <c>style</c> for an entry that sets the <c>--pl-entry-bg</c> /
        /// <c>--pl-entry-fg</c> CSS custom properties for whichever theme colors the
        /// owning game declared. Returns an empty string when the game declared none
        /// (or is no longer installed), so the entry renders with the default theme.
        /// </summary>
        private string EntryStyle(string routeIdentifier)
        {
            _gameColors ??= BuildGameColorMap();
            if (!_gameColors.TryGetValue(routeIdentifier, out var colors))
                return string.Empty;

            var style = string.Empty;
            if (!string.IsNullOrEmpty(colors.Background))
                style += $"--pl-entry-bg:{colors.Background};";
            if (!string.IsNullOrEmpty(colors.Font))
                style += $"--pl-entry-fg:{colors.Font};";
            return style;
        }

        private IReadOnlyDictionary<string, (string? Background, string? Font)> BuildGameColorMap()
        {
            var map = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
            foreach (var module in GameModules)
                map[module.Manifest.RouteIdentifier] = (module.Manifest.BackgroundColor, module.Manifest.FontColor);
            return map;
        }

        private void ToggleExpanded(int index)
        {
            // Remove returns false when the key wasn't present, so this flips state.
            if (!_expanded.Remove(index))
                _expanded.Add(index);
        }

        /// <summary>
        /// Human-friendly elapsed time since <paramref name="playedAt"/> (UTC).
        /// Returns an empty string for an unstamped (default) value.
        /// </summary>
        private static string RelativeTime(DateTimeOffset playedAt)
        {
            if (playedAt == default) return string.Empty;

            var delta = DateTimeOffset.UtcNow - playedAt;
            if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;

            if (delta.TotalSeconds < 60) return "just now";
            if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
            if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
            if (delta.TotalDays < 7) return $"{(int)delta.TotalDays}d ago";
            if (delta.TotalDays < 30) return $"{(int)(delta.TotalDays / 7)}w ago";
            if (delta.TotalDays < 365) return $"{(int)(delta.TotalDays / 30)}mo ago";
            return $"{(int)(delta.TotalDays / 365)}y ago";
        }
    }
}
