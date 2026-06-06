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
        [Parameter] public Guid? CurrentUserId { get; set; }

        // null = "show every word"; set = show only this player's words.
        private Guid? _selectedUserId;

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

            // Search mode shows only the round's shared target list (the subset players were asked
            // to find), scored flat by length to match how the round actually scored. Standard mode
            // shows every board word at its full (non-unique) value.
            bool searchOnly = settings.Mode == GameMode.Search;
            var allowed = searchOnly ? State.SearchList.ToHashSet(StringComparer.Ordinal) : null;
            bool Include(string word) => allowed is null || allowed.Contains(word);
            int Points(string word) => searchOnly
                ? TraceryScorer.BaseScore(word)
                : TraceryScorer.WordScore(word, isUnique: false, settings);

            // The recognizable common-word set the board was built from — NOT the full validation
            // dictionary (which is huge and full of obscure words, and would swamp the overlay).
            // This is the same set the reveal's "nobody found" beat uses to stay readable.
            var display = new Dictionary<string, TracedWord>(StringComparer.Ordinal);
            if (State.BoardFindableWords is { } board)
                foreach (var tw in board.Values)
                    if (Include(tw.Word))
                        display[tw.Word] = tw;

            // …plus any exotic word a player actually banked this round (validation-valid but not
            // in the common set), so a real human find is never hidden. Its path lives in the
            // validation superset. (In Search mode banks are list-only, so this adds nothing.)
            var findable = State.FindableWords;
            if (findable is not null)
                foreach (var o in round?.Outcomes ?? [])
                    foreach (var s in o.WordScores)
                        if (Include(s.Word) && !display.ContainsKey(s.Word) && findable.TryGetValue(s.Word, out var tw))
                            display[s.Word] = tw;

            // Every shown word, scored as the round scored it — "what it's worth".
            _scoredWords = display.Values
                .Select(tw => new ScoredWord(tw.Word, Points(tw.Word), tw.Path))
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
        // Switching the player narrows the word list, so any pinned word might vanish from it —
        // clear the spotlight on every player change so the two panes never disagree.
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
        // Drawing every word's full path stacks dozens of polylines on the same cells and the
        // overlap becomes unreadable. Instead we break all visible paths into undirected
        // cell-to-cell edges and draw each edge once, its thickness growing with how many words
        // share it — so a busy corridor reads as one bold line rather than a tangle.
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

        // Logarithmic thickness with a hard cap so a line never spills past its 1.0-unit cell:
        // count == 1 → Log(1) == 0 → EdgeBase (thin), and growth tapers as more words pile on.
        private const double EdgeBase = 0.08, EdgeScale = 0.06, EdgeMax = 0.28;

        private static string EdgeWidth(int count)
            => Num(Math.Min(EdgeMax, EdgeBase + EdgeScale * Math.Log(count)));

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
            Guid UserId, string DisplayName, int RoundPoints, HashSet<string> Words);
    }
}
