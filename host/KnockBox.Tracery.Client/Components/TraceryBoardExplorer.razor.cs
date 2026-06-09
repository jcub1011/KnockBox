using System.Globalization;
using System.Text;
using KnockBox.Tracery.Contracts;
using KnockBox.Tracery.Models;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Tracery.Client.Components
{
    /// <summary>
    /// The end-of-round board explorer (shown beneath the reveal beats). A pure, client-only
    /// renderer of the projected reveal word set (<see cref="TraceryView.RevealBoardWords"/>, already
    /// scored + filtered server-side) and the latest round outcomes — it never re-solves the board or
    /// receives the full findable-word answer key. Three panes: a player rail ordered by round points,
    /// the letter matrix with one translucent coloured path per word, and a list of every detectable
    /// word ordered by value. Selecting a player narrows the board to the words they found;
    /// hovering/pinning a word spotlights its single path. Each word owns a stable hue so its list
    /// swatch matches its path on the board.
    /// </summary>
    public partial class TraceryBoardExplorer : ComponentBase
    {
        [Parameter, EditorRequired] public TraceryView View { get; set; } = default!;

        // null = "show every word"; set = show only this player's words.
        private Guid? _selectedUserId;

        // The spotlighted word, toggled by clicking a row in the word list. Click-to-toggle (rather
        // than hover) is the single source of truth so it behaves identically on touch and desktop.
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
            var round = View.RoundResults.Count > 0 ? View.RoundResults[^1] : null;
            int roundNo = round?.RoundNumber ?? int.MinValue;
            if (roundNo == _builtForRound) return;

            _builtForRound = roundNo;
            _grid = View.Grid;
            _selectedUserId = null;
            _selectedWord = null;
            Build(round);
        }

        // The word set + scores are already prepared by the server projector (it owns the answer key
        // and the scorer); here we only build the colour map and the per-player word sets.
        private void Build(RoundResult? round)
        {
            _scoredWords = View.RevealBoardWords
                .Select(w => new ScoredWord(w.Word, w.Points, w.Path))
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
        private void SelectPlayer(Guid userId)
        {
            _selectedUserId = _selectedUserId == userId ? null : userId;
            _selectedWord = null;
        }

        private void ShowAll()
        {
            _selectedUserId = null;
            _selectedWord = null;
        }

        private void ToggleWord(string word) => _selectedWord = _selectedWord == word ? null : word;

        private HashSet<string>? SelectedWords
            => _selectedUserId is null
                ? null
                : _playerRows.FirstOrDefault(p => p.UserId == _selectedUserId.Value)?.Words;

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

        // ── Merged edges ──────────────────────────────────────────────────────
        private sealed record MergedEdge(double X1, double Y1, double X2, double Y2, int Count);

        private IReadOnlyList<MergedEdge> MergedEdges()
        {
            var grid = _grid!;
            var counts = new Dictionary<(int Lo, int Hi), int>();
            foreach (var w in VisibleWords())
            {
                var path = w.Path;
                for (int i = 1; i < path.Count; i++)
                {
                    int a = path[i - 1], b = path[i];
                    var key = a < b ? (a, b) : (b, a);
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
            }

            var list = new List<MergedEdge>(counts.Count);
            foreach (var (key, count) in counts)
            {
                var (ra, ca) = grid.FromCellId(key.Lo);
                var (rb, cb) = grid.FromCellId(key.Hi);
                list.Add(new MergedEdge(ca + 0.5, ra + 0.5, cb + 0.5, rb + 0.5, count));
            }
            return list;
        }

        // Logarithmic thickness with a hard cap so a line never spills past its 1.0-unit cell.
        private const double EdgeBase = 0.08, EdgeScale = 0.06, EdgeMax = 0.28;

        private static string EdgeWidth(int count)
            => Num(Math.Min(EdgeMax, EdgeBase + EdgeScale * Math.Log(count)));

        private string Stroke(string word, double alpha)
            => $"hsla({_hueByWord.GetValueOrDefault(word):0}, 72%, 62%, {Num(alpha)})";

        private string Swatch(string word)
            => $"hsla({_hueByWord.GetValueOrDefault(word):0}, 72%, 62%, 0.9)";

        private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // Stable, well-spread hue per word: FNV-1a hash spun by the golden angle.
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
            Guid UserId, string DisplayName, int RoundPoints, HashSet<string> Words);
    }
}
