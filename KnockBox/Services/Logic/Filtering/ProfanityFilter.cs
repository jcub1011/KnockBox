namespace KnockBox.Services.Logic.Filtering
{
    public class ProfanityFilter : IProfanityFilter
    {
        private const string ProfanityResourceName = "KnockBox.Data.Statics.Profanities.English.txt";
        private static readonly Lazy<Automaton> AutomatonLazy = new(Automaton.Build, LazyThreadSafetyMode.ExecutionAndPublication);

        public ValueTask<List<ProfanityMatch>?> ExtractProfanitiesAsync(string text, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(text))
            {
                return ValueTask.FromResult<List<ProfanityMatch>?>(null);
            }

            var matches = AutomatonLazy.Value.Find(text, ct);
            return ValueTask.FromResult(matches.Count == 0 ? null : matches);
        }

        private sealed class Automaton
        {
            private readonly List<Node> _nodes;

            private Automaton(List<Node> nodes)
            {
                _nodes = nodes;
            }

            public static Automaton Build()
            {
                var nodes = new List<Node> { Node.CreateRoot() };

                using var stream = typeof(ProfanityFilter).Assembly.GetManifestResourceStream(ProfanityResourceName)
                    ?? throw new InvalidOperationException($"Embedded resource not found: {ProfanityResourceName}");
                using var reader = new StreamReader(stream);

                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    var word = line.Trim();
                    if (string.IsNullOrWhiteSpace(word))
                    {
                        continue;
                    }

                    AddWord(nodes, word);
                }

                BuildFailureLinks(nodes);
                return new Automaton(nodes);
            }

            public List<ProfanityMatch> Find(string text, CancellationToken ct)
            {
                var results = new List<ProfanityMatch>();
                var state = 0;

                for (var i = 0; i < text.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var c = char.ToLowerInvariant(text[i]);

                    while (state != 0 && !_nodes[state].Next.TryGetValue(c, out _))
                    {
                        state = _nodes[state].Fail;
                    }

                    if (_nodes[state].Next.TryGetValue(c, out var next))
                    {
                        state = next;
                    }

                    var outputs = _nodes[state].Outputs;
                    if (outputs.Count == 0)
                    {
                        continue;
                    }

                    foreach (var length in outputs)
                    {
                        var start = i - length + 1;
                        if (start >= 0)
                        {
                            results.Add(new ProfanityMatch(start, length));
                        }
                    }
                }

                return FilterContainedMatches(results);
            }

            private static List<ProfanityMatch> FilterContainedMatches(List<ProfanityMatch> matches)
            {
                if (matches.Count < 2)
                {
                    return matches;
                }

                matches.Sort((a, b) =>
                {
                    var cmp = a.StartIndex.CompareTo(b.StartIndex);
                    return cmp != 0 ? cmp : b.Length.CompareTo(a.Length);
                });

                var filtered = new List<ProfanityMatch>(matches.Count);
                var currentMaxEndExclusive = -1;

                foreach (var match in matches)
                {
                    var endExclusive = match.StartIndex + match.Length;

                    if (currentMaxEndExclusive <= match.StartIndex)
                    {
                        filtered.Add(match);
                        currentMaxEndExclusive = endExclusive;
                        continue;
                    }

                    if (endExclusive > currentMaxEndExclusive)
                    {
                        filtered.Add(match);
                        currentMaxEndExclusive = endExclusive;
                    }
                }

                return filtered;
            }

            private static void AddWord(List<Node> nodes, string word)
            {
                var state = 0;

                foreach (var rawChar in word)
                {
                    var c = char.ToLowerInvariant(rawChar);

                    if (!nodes[state].Next.TryGetValue(c, out var next))
                    {
                        next = nodes.Count;
                        nodes[state].Next[c] = next;
                        nodes.Add(Node.Create());
                    }

                    state = next;
                }

                nodes[state].Outputs.Add(word.Length);
            }

            private static void BuildFailureLinks(List<Node> nodes)
            {
                var queue = new Queue<int>();

                foreach (var next in nodes[0].Next.Values)
                {
                    nodes[next].Fail = 0;
                    queue.Enqueue(next);
                }

                while (queue.Count > 0)
                {
                    var state = queue.Dequeue();

                    foreach (var (c, next) in nodes[state].Next)
                    {
                        var fail = nodes[state].Fail;

                        while (fail != 0 && !nodes[fail].Next.TryGetValue(c, out _))
                        {
                            fail = nodes[fail].Fail;
                        }

                        if (nodes[fail].Next.TryGetValue(c, out var failNext))
                        {
                            nodes[next].Fail = failNext;
                        }
                        else
                        {
                            nodes[next].Fail = 0;
                        }

                        if (nodes[nodes[next].Fail].Outputs.Count > 0)
                        {
                            nodes[next].Outputs.AddRange(nodes[nodes[next].Fail].Outputs);
                        }

                        queue.Enqueue(next);
                    }
                }
            }

            private sealed class Node
            {
                public Dictionary<char, int> Next { get; } = new();
                public int Fail { get; set; }
                public List<int> Outputs { get; } = new();

                public static Node Create() => new();
                public static Node CreateRoot() => new();
            }
        }
    }
}
