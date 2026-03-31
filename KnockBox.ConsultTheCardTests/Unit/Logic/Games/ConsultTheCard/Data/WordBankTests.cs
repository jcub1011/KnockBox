using KnockBox.Services.Logic.Games.ConsultTheCard.Data;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.ConsultTheCardTests.Unit.Logic.Games.ConsultTheCard.Data
{
    [TestClass]
    public class WordBankTests
    {
        private Mock<ILogger> _loggerMock = default!;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [TestMethod]
        public void Load_ReturnsNonEmptyList()
        {
            var groups = WordBank.Load(_loggerMock.Object);

            Assert.IsTrue(groups.Count >= 50, $"Expected at least 50 word groups, got {groups.Count}.");
            Assert.IsTrue(groups.Count <= 100, $"Expected at most 100 word groups, got {groups.Count}.");
        }

        [TestMethod]
        public void Load_AllGroupsHaveAtLeastTwoWords()
        {
            var groups = WordBank.Load(_loggerMock.Object);

            foreach (var group in groups)
            {
                Assert.IsTrue(group.Words.Length >= 2,
                    $"Word group [{string.Join(", ", group.Words)}] has fewer than 2 words.");
            }
        }

        [TestMethod]
        public void Load_NoWordsHaveLeadingOrTrailingWhitespace()
        {
            var groups = WordBank.Load(_loggerMock.Object);

            foreach (var group in groups)
            {
                foreach (var word in group.Words)
                {
                    Assert.AreEqual(word.Trim(), word,
                        $"Word '{word}' has leading or trailing whitespace.");
                }
            }
        }

        [TestMethod]
        public void Load_NoEmptyWords()
        {
            var groups = WordBank.Load(_loggerMock.Object);

            foreach (var group in groups)
            {
                foreach (var word in group.Words)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(word),
                        "Found an empty or whitespace-only word.");
                }
            }
        }
    }
}
