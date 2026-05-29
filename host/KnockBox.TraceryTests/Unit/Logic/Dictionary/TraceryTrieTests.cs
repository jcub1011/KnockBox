using KnockBox.Tracery.Services.Logic.Dictionary;
using KnockBox.WordService.Contracts;
using KnockBox.WordService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.Tracery.Tests.Unit.Logic.Dictionary
{
    [TestClass]
    public class TraceryTrieTests
    {
        // ── In-memory trie: IsWord / IsPrefix truth table ───────────────────────

        [TestMethod]
        public void IsWord_TrueForInsertedWords_FalseForOthers()
        {
            var trie = TraceryTrie.FromWords("cat", "cats", "dog", "tracery");

            Assert.IsTrue(trie.IsWord("cat"));
            Assert.IsTrue(trie.IsWord("cats"));
            Assert.IsTrue(trie.IsWord("dog"));
            Assert.IsTrue(trie.IsWord("tracery"));

            Assert.IsFalse(trie.IsWord("ca"));      // proper prefix, not a word
            Assert.IsFalse(trie.IsWord("catz"));    // not inserted
            Assert.IsFalse(trie.IsWord("do"));
            Assert.IsFalse(trie.IsWord("trace"));   // prefix of tracery, not its own word
            Assert.IsFalse(trie.IsWord(""));        // empty is never a word
        }

        [TestMethod]
        public void IsPrefix_TrueForAnyPrefixOfAWord()
        {
            var trie = TraceryTrie.FromWords("cats", "dog");

            Assert.IsTrue(trie.IsPrefix(""));    // empty is trivially a prefix
            Assert.IsTrue(trie.IsPrefix("c"));
            Assert.IsTrue(trie.IsPrefix("ca"));
            Assert.IsTrue(trie.IsPrefix("cat"));
            Assert.IsTrue(trie.IsPrefix("cats"));
            Assert.IsTrue(trie.IsPrefix("d"));

            Assert.IsFalse(trie.IsPrefix("catz"));
            Assert.IsFalse(trie.IsPrefix("x"));
            Assert.IsFalse(trie.IsPrefix("doge"));  // extends past the word
        }

        [TestMethod]
        public void Lookups_AreCaseInsensitive()
        {
            var trie = TraceryTrie.FromWords("cat");

            Assert.IsTrue(trie.IsWord("CAT"));
            Assert.IsTrue(trie.IsWord("Cat"));
            Assert.IsTrue(trie.IsWord("cAt"));
            Assert.IsTrue(trie.IsPrefix("CA"));
        }

        [TestMethod]
        public void NonAsciiOrNonLetter_ReturnsFalse()
        {
            var trie = TraceryTrie.FromWords("cat");

            Assert.IsFalse(trie.IsWord("cát"));     // accented, non-ASCII
            Assert.IsFalse(trie.IsWord("ca7"));     // digit
            Assert.IsFalse(trie.IsWord("c-t"));     // punctuation
            Assert.IsFalse(trie.IsPrefix("café"));
        }

        // ── Threaded transitions agree with the span queries ────────────────────
        // The solver walks the trie one letter at a time via Transition/IsWordNode rather
        // than re-walking from the root with IsPrefix/IsWord. The two paths must agree, which
        // also exercises the CSR packing (edge slices, ascending-letter order).

        [TestMethod]
        public void Transition_FollowsEdges_AndAgreesWithSpanQueries()
        {
            var trie = TraceryTrie.FromWords("cat", "cats", "dog", "tracery");

            foreach (var word in new[] { "cat", "cats", "dog", "tracery", "trace", "catz", "x" })
            {
                int node = TraceryTrie.Root;
                bool prefixAlive = true;
                for (int i = 0; i < word.Length; i++)
                {
                    node = trie.Transition(node, word[i]);
                    if (node < 0) { prefixAlive = false; break; }
                }

                Assert.AreEqual(trie.IsPrefix(word), prefixAlive,
                    $"Transition walk disagreed with IsPrefix for \"{word}\".");
                Assert.AreEqual(trie.IsWord(word), prefixAlive && trie.IsWordNode(node),
                    $"Transition walk disagreed with IsWord for \"{word}\".");
            }
        }

        [TestMethod]
        public void Transition_RejectsNonLetters_AndUnknownEdges()
        {
            var trie = TraceryTrie.FromWords("cat");

            int c = trie.Transition(TraceryTrie.Root, 'c');
            Assert.IsTrue(c >= 0);
            Assert.IsTrue(trie.Transition(TraceryTrie.Root, 'C') >= 0, "Transition should fold case.");

            Assert.AreEqual(-1, trie.Transition(TraceryTrie.Root, 'z'), "No edge for an absent letter.");
            Assert.AreEqual(-1, trie.Transition(TraceryTrie.Root, '7'), "No edge for a digit.");
            Assert.AreEqual(-1, trie.Transition(c, '7'), "No edge for a digit mid-walk.");
        }

        // ── Real dictionary build via the word service ──────────────────────────

        [TestMethod]
        public void BuildFrom_RealDictionary_KnownWordsResolve()
        {
            var svc = new WordListService(NullLogger<WordListService>.Instance);
            var trie = TraceryTrie.BuildFrom(svc, minWordLength: 3);

            // A handful of common words that must exist in the full dictionary.
            Assert.IsTrue(trie.IsWord("word"));
            Assert.IsTrue(trie.IsWord("game"));
            Assert.IsTrue(trie.IsWord("trace"));
            Assert.IsTrue(trie.IsPrefix("trac"));

            // Gibberish must not.
            Assert.IsFalse(trie.IsWord("zzzzz"));
            Assert.IsFalse(trie.IsPrefix("qx"));
        }

        [TestMethod]
        public void BuildFrom_HonorsMinWordLength_DropsShortWords()
        {
            var svc = new WordListService(NullLogger<WordListService>.Instance);
            var trie = TraceryTrie.BuildFrom(svc, minWordLength: 5);

            // "cat" is in the dictionary but below the floor, so it was never inserted;
            // it remains a prefix though, because longer words start with it.
            Assert.IsFalse(trie.IsWord("cat"));
            Assert.IsTrue(trie.IsPrefix("cat"));

            // A 5+ letter word is unaffected.
            Assert.IsTrue(trie.IsWord("trace"));
        }
    }
}
