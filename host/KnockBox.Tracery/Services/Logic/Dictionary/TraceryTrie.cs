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
    /// Both queries are span-based and allocation-free: the lookup walks node-indexed
    /// arrays without touching the heap. Letters fold ASCII upper→lower; any non-ASCII
    /// or non-letter character makes the query return false.
    /// </summary>
    internal sealed class TraceryTrie
    {
        // Node-parallel arrays. Node 0 is the root.
        //   _children[n] is null until node n gains its first child, then a 26-slot
        //   array of child node ids indexed by (letter - 'a'). A slot value of 0 means
        //   "no child" — safe because node 0 (the root) is never anyone's child.
        //   _isWord[n] marks a node as the end of an inserted word.
        // Leaf nodes never allocate a children array, which is the bulk of the nodes.
        private int[]?[] _children;
        private bool[] _isWord;
        private int _count;

        private TraceryTrie(int capacity)
        {
            _children = new int[capacity][];
            _isWord = new bool[capacity];
            _count = 1; // reserve node 0 as the root
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
            int node = 0;
            foreach (char c in word)
            {
                int idx = LetterIndex(c);
                if (idx < 0) return -1;
                var kids = _children[node];
                if (kids is null) return -1;
                int child = kids[idx];
                if (child == 0) return -1;
                node = child;
            }
            return node;
        }

        // Folds ASCII upper→lower and maps a-z to 0..25; -1 for anything else
        // (digits, punctuation, and any non-ASCII character such as accented letters).
        private static int LetterIndex(char c)
        {
            if (c >= 'a' && c <= 'z') return c - 'a';
            if (c >= 'A' && c <= 'Z') return c - 'A';
            return -1;
        }

        private void Insert(ReadOnlySpan<byte> asciiWord)
        {
            int node = 0;
            foreach (byte b in asciiWord)
            {
                int idx = LetterIndex((char)b);
                if (idx < 0) return; // skip any word with a non a-z byte rather than corrupt the trie
                var kids = _children[node];
                if (kids is null)
                {
                    kids = new int[26];
                    _children[node] = kids;
                }
                int child = kids[idx];
                if (child == 0)
                {
                    child = NewNode();
                    // Re-read: NewNode may have resized the outer jagged array, but the
                    // inner `kids` array object is unchanged, and _children[node] still
                    // points at it, so writing through `kids` is correct.
                    kids[idx] = child;
                }
                node = child;
            }
            _isWord[node] = true;
        }

        private int NewNode()
        {
            if (_count == _children.Length)
            {
                int next = _children.Length * 2;
                Array.Resize(ref _children, next);
                Array.Resize(ref _isWord, next);
            }
            return _count++;
        }

        /// <summary>
        /// Builds the trie from the full dictionary, inserting only words of length
        /// ≥ <paramref name="minWordLength"/> so the solver's search space is pruned at
        /// the source. Words come back as raw lowercase ASCII bytes and are inserted
        /// immediately (the spans alias the service's internal buffer).
        /// </summary>
        public static TraceryTrie BuildFrom(IWordListService svc, int minWordLength)
        {
            // ~64k nodes to start; grows by doubling. Sized to dodge the early resizes
            // on the large full-dictionary load without over-allocating.
            var trie = new TraceryTrie(1 << 16);
            const WordPoolMode mode = WordPoolMode.FullDictionary;
            foreach (int len in svc.GetAvailableLengths(mode))
            {
                if (len < minWordLength) continue;
                int count = svc.GetWordCount(mode, len);
                for (int i = 0; i < count; i++)
                    trie.Insert(svc.GetWord(mode, len, i));
            }
            return trie;
        }

        /// <summary>
        /// Test-only builder: assembles a trie from an in-memory word list so solver and
        /// trie tests can pin down an exact, tiny dictionary without loading the real one.
        /// </summary>
        internal static TraceryTrie FromWords(params string[] words)
        {
            var trie = new TraceryTrie(64);
            Span<byte> buffer = stackalloc byte[64];
            foreach (var word in words)
            {
                if (word.Length > buffer.Length) throw new ArgumentException($"Test word too long: {word}");
                for (int i = 0; i < word.Length; i++) buffer[i] = (byte)word[i];
                trie.Insert(buffer[..word.Length]);
            }
            return trie;
        }
    }
}
