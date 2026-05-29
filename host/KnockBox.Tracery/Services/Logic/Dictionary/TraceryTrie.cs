using KnockBox.WordService.Contracts;

namespace KnockBox.Tracery.Services.Logic.Dictionary
{
    /// <summary>
    /// A prefix tree over the lowercase-ASCII dictionary, owned and built once by the
    /// singleton <c>TraceryGameEngine</c>. It backs the solver's two hot-path queries:
    /// <see cref="IsWord"/> (is this exact string a dictionary word) and
    /// <see cref="IsPrefix"/> (could any word start with this string) — the latter is
    /// what lets the DFS abandon a partial path the instant it goes dead.
    ///
    /// The trie is stored in a compressed-sparse-row (CSR) layout: every node's outgoing
    /// edges live in a contiguous slice of shared flat arrays rather than a per-node
    /// <c>int[26]</c>. The average node has barely more than one child, so the old fixed
    /// array spent ~104 bytes (plus an object header) per node to hold a single id; the CSR
    /// form stores exactly one entry per real edge and drops the ~1M tiny array objects,
    /// cutting steady-state memory by an order of magnitude while keeping lookups O(1)-ish
    /// (a scan over a handful of edges). The pointer trie still exists transiently inside
    /// <see cref="Builder"/> during construction, then is flattened and discarded.
    ///
    /// Both queries are span-based and allocation-free: the lookup walks node-indexed
    /// arrays without touching the heap. Letters fold ASCII upper→lower; any non-ASCII
    /// or non-letter character makes the query return false.
    /// </summary>
    internal sealed class TraceryTrie
    {
        // CSR layout. Node 0 is the root.
        //   Node n's outgoing edges occupy the slice [_nodeStart[n], _nodeStart[n + 1]):
        //   _labels[i] is the edge letter ('a'..'z') and _targets[i] the child node id.
        //   Edges within a node are stored in ascending-letter order.
        //   _isWord[n] marks node n as the end of an inserted word.
        private readonly byte[] _labels;
        private readonly int[] _targets;
        private readonly int[] _nodeStart;
        private readonly bool[] _isWord;

        /// <summary>The start node for any walk; the solver threads this in at depth 0.</summary>
        internal const int Root = 0;

        private TraceryTrie(byte[] labels, int[] targets, int[] nodeStart, bool[] isWord)
        {
            _labels = labels;
            _targets = targets;
            _nodeStart = nodeStart;
            _isWord = isWord;
        }

        /// <summary>True if <paramref name="word"/> is an inserted dictionary word.</summary>
        public bool IsWord(ReadOnlySpan<char> word)
        {
            int node = Walk(word);
            return node >= 0 && _isWord[node];
        }

        /// <summary>
        /// True if some inserted word starts with <paramref name="word"/> (an empty span
        /// is trivially a prefix). The solver calls this after appending each letter and
        /// prunes the branch the moment it returns false.
        /// </summary>
        public bool IsPrefix(ReadOnlySpan<char> word) => Walk(word) >= 0;

        // Returns the node id reached by following word from the root, or -1 if any
        // character is a non-letter or the path doesn't exist in the trie.
        private int Walk(ReadOnlySpan<char> word)
        {
            int node = Root;
            foreach (char c in word)
            {
                node = Transition(node, c);
                if (node < 0) return -1;
            }
            return node;
        }

        /// <summary>
        /// Follows the edge labelled <paramref name="letter"/> out of <paramref name="node"/>,
        /// returning the child node id, or -1 if <paramref name="letter"/> is not an ASCII
        /// letter or the node has no such edge. The solver threads the returned node into the
        /// next DFS step, so each step costs one edge scan instead of a re-walk from the root.
        /// </summary>
        internal int Transition(int node, char letter)
        {
            int idx = LetterIndex(letter);
            if (idx < 0) return -1;
            byte target = (byte)('a' + idx);
            int end = _nodeStart[node + 1];
            for (int i = _nodeStart[node]; i < end; i++)
            {
                if (_labels[i] == target) return _targets[i];
            }
            return -1;
        }

        /// <summary>True if <paramref name="node"/> is the end of an inserted word.</summary>
        internal bool IsWordNode(int node) => _isWord[node];

        // Folds ASCII upper→lower and maps a-z to 0..25; -1 for anything else
        // (digits, punctuation, and any non-ASCII character such as accented letters).
        private static int LetterIndex(char c)
        {
            if (c >= 'a' && c <= 'z') return c - 'a';
            if (c >= 'A' && c <= 'Z') return c - 'A';
            return -1;
        }

        /// <summary>
        /// Builds the trie from the full dictionary, inserting only words of length
        /// ≥ <paramref name="minWordLength"/> so the solver's search space is pruned at
        /// the source. Words come back as raw lowercase ASCII bytes and are inserted
        /// immediately (the spans alias the service's internal buffer).
        /// </summary>
        public static TraceryTrie BuildFrom(IWordListService svc, int minWordLength)
        {
            const WordPoolMode mode = WordPoolMode.FullDictionary;

            // Pre-size the builder from the word count to dodge doubling-resize churn during
            // the large full-dictionary load. The trie has ~2.8 nodes per word on this
            // dictionary, so ×3 lands just above the real node count without over-allocating;
            // the builder still grows by doubling if the estimate is low. Floor of 64 keeps
            // tiny/empty pools from allocating a degenerate builder.
            long totalWords = 0;
            foreach (int len in svc.GetAvailableLengths(mode))
                if (len >= minWordLength) totalWords += svc.GetWordCount(mode, len);
            int capacity = (int)Math.Min(int.MaxValue, Math.Max(64, totalWords * 3));

            var builder = new Builder(capacity);
            foreach (int len in svc.GetAvailableLengths(mode))
            {
                if (len < minWordLength) continue;
                int count = svc.GetWordCount(mode, len);
                for (int i = 0; i < count; i++)
                    builder.Insert(svc.GetWord(mode, len, i));
            }
            return builder.Pack();
        }

        /// <summary>
        /// Test-only builder: assembles a trie from an in-memory word list so solver and
        /// trie tests can pin down an exact, tiny dictionary without loading the real one.
        /// </summary>
        internal static TraceryTrie FromWords(params string[] words)
        {
            var builder = new Builder(64);
            Span<byte> buffer = stackalloc byte[64];
            foreach (var word in words)
            {
                if (word.Length > buffer.Length) throw new ArgumentException($"Test word too long: {word}");
                for (int i = 0; i < word.Length; i++) buffer[i] = (byte)word[i];
                builder.Insert(buffer[..word.Length]);
            }
            return builder.Pack();
        }

        /// <summary>
        /// Transient pointer trie used only during construction, stored as a sibling-linked
        /// list rather than an <c>int[26]</c> per node: <c>_firstChild[n]</c> is the id of
        /// node n's first child (0 = none), <c>_nextSibling[c]</c> is c's next sibling
        /// (0 = end of chain), and <c>_label[c]</c> is the (canonical lowercase) letter on
        /// the parent→c edge. The 0 sentinel is safe because node 0 (the root) is never
        /// anyone's child. The average node has barely more than one child, so these flat
        /// node-sized arrays cost a few MB total instead of the ~100 MB of tiny
        /// <c>int[26]</c> objects the old layout allocated and immediately threw to GC.
        /// Once every word is inserted, <see cref="Pack"/> flattens it into the CSR arrays
        /// and the builder is dropped, so it never reaches steady state.
        /// </summary>
        private sealed class Builder
        {
            private int[] _firstChild;
            private int[] _nextSibling;
            private byte[] _label;
            private bool[] _isWord;
            private int _count;

            public Builder(int capacity)
            {
                if (capacity < 1) capacity = 1;
                _firstChild = new int[capacity];
                _nextSibling = new int[capacity];
                _label = new byte[capacity];
                _isWord = new bool[capacity];
                _count = 1; // reserve node 0 as the root
            }

            public void Insert(ReadOnlySpan<byte> asciiWord)
            {
                int node = 0;
                foreach (byte b in asciiWord)
                {
                    int idx = LetterIndex((char)b);
                    if (idx < 0) return; // skip any word with a non a-z byte rather than corrupt the trie
                    byte label = (byte)('a' + idx); // canonical lowercase, so input case folds

                    // Walk the (short) sibling chain looking for an existing edge on this letter.
                    int child = 0;
                    for (int c = _firstChild[node]; c != 0; c = _nextSibling[c])
                    {
                        if (_label[c] == label) { child = c; break; }
                    }

                    if (child == 0)
                    {
                        child = NewNode(); // may resize the parallel arrays
                        _label[child] = label;
                        _nextSibling[child] = _firstChild[node]; // prepend — O(1)
                        _firstChild[node] = child;
                    }
                    node = child;
                }
                _isWord[node] = true;
            }

            private int NewNode()
            {
                if (_count == _firstChild.Length)
                {
                    int next = _firstChild.Length * 2;
                    Array.Resize(ref _firstChild, next);
                    Array.Resize(ref _nextSibling, next);
                    Array.Resize(ref _label, next);
                    Array.Resize(ref _isWord, next);
                }
                return _count++;
            }

            // Flattens the pointer trie into CSR arrays: one entry per real edge, grouped by
            // source node in ascending-letter order, with _nodeStart (length _count + 1)
            // holding each node's slice bounds via a prefix sum of out-degrees.
            public TraceryTrie Pack()
            {
                int nodeCount = _count;
                var nodeStart = new int[nodeCount + 1];

                // Pass 1: out-degree = sibling-chain length. (Total edges = nodeCount - 1,
                // since every non-root node is exactly one parent's child.)
                int edgeCount = 0;
                for (int n = 0; n < nodeCount; n++)
                {
                    int degree = 0;
                    for (int c = _firstChild[n]; c != 0; c = _nextSibling[c]) degree++;
                    nodeStart[n + 1] = degree;
                    edgeCount += degree;
                }

                // Prefix-sum the out-degrees into start offsets.
                for (int n = 0; n < nodeCount; n++)
                    nodeStart[n + 1] += nodeStart[n];

                // Pass 2: emit each node's edges in ascending-letter order. The chain is in
                // reverse-insertion order, so gather it (≤26 entries) and insertion-sort by
                // label to reproduce the documented ascending emission.
                var labels = new byte[edgeCount];
                var targets = new int[edgeCount];
                var isWord = new bool[nodeCount];
                Span<byte> edgeLabels = stackalloc byte[26];
                Span<int> edgeTargets = stackalloc int[26];
                for (int n = 0; n < nodeCount; n++)
                {
                    isWord[n] = _isWord[n];

                    int deg = 0;
                    for (int c = _firstChild[n]; c != 0; c = _nextSibling[c])
                    {
                        edgeLabels[deg] = _label[c];
                        edgeTargets[deg] = c;
                        deg++;
                    }

                    // Insertion sort ascending by label (deg ≤ 26, usually 1–2).
                    for (int i = 1; i < deg; i++)
                    {
                        byte lab = edgeLabels[i];
                        int tgt = edgeTargets[i];
                        int j = i - 1;
                        while (j >= 0 && edgeLabels[j] > lab)
                        {
                            edgeLabels[j + 1] = edgeLabels[j];
                            edgeTargets[j + 1] = edgeTargets[j];
                            j--;
                        }
                        edgeLabels[j + 1] = lab;
                        edgeTargets[j + 1] = tgt;
                    }

                    int w = nodeStart[n];
                    for (int i = 0; i < deg; i++)
                    {
                        labels[w] = edgeLabels[i];
                        targets[w] = edgeTargets[i];
                        w++;
                    }
                }

                return new TraceryTrie(labels, targets, nodeStart, isWord);
            }
        }
    }
}
