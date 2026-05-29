using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic;
using KnockBox.Tracery.Services.State.Games;

namespace KnockBox.Tracery.Components
{
    /// <summary>
    /// The end-of-round board explorer (shown beneath the reveal beats). A pure, client-only
    /// renderer of data already materialized on <see cref="TraceryGameState"/> at reveal time —
    /// it never re-solves the board or hits the server. Three panes: a player rail ordered by
    /// round points, the letter matrix with one translucent coloured path per word, and a list
    /// of every detectable word ordered by value. Selecting a player narrows the board to the
    /// words they found; hovering/pinning a word spotlights its single path. Each word owns a
    /// stable hue so its list swatch matches its path on the board.
    /// </summary>
    public partial class TraceryBoardExplorer : ComponentBase
    {
        [Parameter, EditorRequired] public TraceryGameState State { get; set; } = default!;

        /// <summary>The viewer's user id, used only to tag their row with "YOU". Optional.</summary>
        [Parameter] public string? CurrentUserId { get; set; }

        // null = "show every word"; set = show only this player's words.
        private string? _selectedUserId;

        // The spotlighted word, toggled by clicking a row in the word list. Click-to-toggle (rather
        // than hover) is the single source of truth so it behaves identically on touch and desktop —
        // clicking the already-selected word reliably clears it.
        private string? _selectedWord;
        private string? Highlighted => _selectedWord;

        private Grid? _grid;
        private IReadOnlyList<ScoredWord> _scoredWords = [];
        private IReadOnlyList<PlayerRow> _playerRows = [];
        private Dictionary<string, double> _hueByWord = new(StringComparer.Ordinal);

        // The round we last built for, so we only recompute (and reset selection) on a new round.
        private int _builtForRound = int.MinValue;

        protected override void OnParametersSet()
        {
            _grid = State.CurrentGrid;
            var round = State.RoundResults.Count > 0 ? State.RoundResults[^1] : null;
            int roundNo = round?.RoundNumber ?? int.MinValue;
            if (roundNo == _builtForRound) return;

            _builtForRound = roundNo;
            _selectedUserId = null;
            _selectedWord = null;
            Build(round);
        }

        private void Build(RoundResult? round)
        {
            var settings = State.Settings;

            // The recognizable common-word set the board was built from — NOT the full validation
            // dictionary (which is huge and full of obscure words, and would swamp the overlay).
            // This is the same set the reveal's "nobody found" beat uses to stay readable.
            var display = new Dictionary<string, TracedWord>(StringComparer.Ordinal);
            if (State.BoardFindableWords is { } board)
                foreach (var tw in board.Values)
                    display[tw.Word] = tw;

            // …plus any exotic word a player actually banked this round (validation-valid but not
            // in the common set), so a real human find is never hidden. Its path lives in the
            // validation superset.
            var findable = State.FindableWords;
            if (findable is not null)
                foreach (var o in round?.Outcomes ?? [])
                    foreach (var s in o.WordScores)
                        if (!display.ContainsKey(s.Word) && findable.TryGetValue(s.Word, out var tw))
                            display[s.Word] = tw;

            // Every shown word, scored at its plain (non-unique) value — "what it's worth".
            _scoredWords = display.Values
                .Select(tw => new ScoredWord(
                    tw.Word,
                    TraceryScorer.WordScore(tw.Word, isUnique: false, settings),
                    tw.Path))
                .OrderByDescending(w => w.Points)
                .ThenByDescending(w => w.Word.Length)
                .ThenBy(w => w.Word, StringComparer.Ordinal)
                .ToList();

            _hueByWord = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var w in _scoredWords)
                _hueByWord[w.Word] = HueFor(w.Word);

            _playerRows = (round?.Outcomes ?? [])
                .Select(o => new PlayerRow(
                    o.UserId,
                    o.DisplayName,
                    o.PointsAwarded,
                    o.WordScores.Select(s => s.Word).ToHashSet(StringComparer.Ordinal)))
                .OrderByDescending(p => p.RoundPoints)
                .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ── Selection / spotlight state ───────────────────────────────────────
        private void SelectPlayer(string userId)
            => _selectedUserId = _selectedUserId == userId ? null : userId;

        private void ShowAll() => _selectedUserId = null;

        private void ToggleWord(string word) => _selectedWord = _selectedWord == word ? null : word;

        private HashSet<string>? SelectedWords
            => _selectedUserId is null
                ? null
                : _playerRows.FirstOrDefault(p => p.UserId == _selectedUserId)?.Words;

        // The paths drawn on the board right now: all words, or just the selected player's.
        private IEnumerable<ScoredWord> VisibleWords()
        {
            if (SelectedWords is not { } words) return _scoredWords;
            return _scoredWords.Where(w => words.Contains(w.Word));
        }

        // Cells belonging to the spotlighted word, lit under the matrix for readability.
        private HashSet<int> LitCells()
            => Highlighted is { } hw && _scoredWords.FirstOrDefault(w => w.Word == hw) is { } sw
                ? sw.Path.ToHashSet()
                : [];

        // ── Geometry / colour helpers ─────────────────────────────────────────
        private string LinePoints(IReadOnlyList<int> path)
        {
            var grid = _grid!;
            var sb = new StringBuilder();
            foreach (int id in path)
            {
                var (r, c) = grid.FromCellId(id);
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(Num(c + 0.5)).Append(',').Append(Num(r + 0.5));
            }
            return sb.ToString();
        }

        private (string x, string y) Start(IReadOnlyList<int> path)
        {
            var (r, c) = _grid!.FromCellId(path[0]);
            return (Num(c + 0.5), Num(r + 0.5));
        }

        // Stroke opacity: kept low so the letters read through the lines. A single spotlight fades
        // everything else right back; otherwise paths are faintest when the whole board is shown
        // and a touch bolder once a player narrows it down.
        private double AlphaFor(string word)
        {
            if (Highlighted is { } hw) return word == hw ? 0.85 : 0.05;
            return _selectedUserId is null ? 0.28 : 0.5;
        }

        private string Stroke(string word, double alpha)
            => $"hsla({_hueByWord.GetValueOrDefault(word):0}, 72%, 62%, {Num(alpha)})";

        private string Swatch(string word)
            => $"hsla({_hueByWord.GetValueOrDefault(word):0}, 72%, 62%, 0.9)";

        private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // Stable, well-spread hue per word: FNV-1a hash spun by the golden angle, so colours are
        // distinct between neighbours yet identical for the same word across every render.
        private static double HueFor(string word)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (char ch in word) { h ^= ch; h *= 16777619u; }
                return (h * 137.508) % 360.0;
            }
        }

        private sealed record ScoredWord(string Word, int Points, IReadOnlyList<int> Path);

        private sealed record PlayerRow(
            string UserId, string DisplayName, int RoundPoints, HashSet<string> Words);
    }
}
