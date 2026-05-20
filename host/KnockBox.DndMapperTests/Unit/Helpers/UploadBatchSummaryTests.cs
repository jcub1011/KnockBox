using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Pages.Components;

namespace KnockBox.DndMapperTests.Unit.Helpers
{
    [TestClass]
    public class UploadBatchSummaryTests
    {
        [TestMethod]
        public void AllSuccess_SingleFile_ReportsSingularSuccess()
        {
            var s = UploadBatchSummary.Build(successes: 1, failures: []);
            Assert.AreEqual("Image uploaded.", s.Message);
            Assert.AreEqual(DndMapperToastTone.Success, s.Tone);
        }

        [TestMethod]
        public void AllSuccess_MultipleFiles_ReportsPluralCount()
        {
            var s = UploadBatchSummary.Build(successes: 3, failures: []);
            Assert.AreEqual("3 images uploaded.", s.Message);
            Assert.AreEqual(DndMapperToastTone.Success, s.Tone);
        }

        [TestMethod]
        public void AllFail_SingleFile_ReportsFailureVerbatim()
        {
            var s = UploadBatchSummary.Build(successes: 0, failures: new[] { "map.png: exceeds 100 MB" });
            Assert.AreEqual("map.png: exceeds 100 MB", s.Message);
            Assert.AreEqual(DndMapperToastTone.Danger, s.Tone);
        }

        [TestMethod]
        public void AllFail_MultipleFiles_ReportsBatchFailure()
        {
            var s = UploadBatchSummary.Build(successes: 0, failures: new[] { "a: bad", "b: bad", "c: bad" });
            Assert.AreEqual("All 3 uploads failed.", s.Message);
            Assert.AreEqual(DndMapperToastTone.Danger, s.Tone);
        }

        [TestMethod]
        public void Mixed_ReportsMixedTallyAsWarning()
        {
            var s = UploadBatchSummary.Build(successes: 2, failures: new[] { "x: bad" });
            Assert.AreEqual("2 uploaded, 1 failed.", s.Message);
            Assert.AreEqual(DndMapperToastTone.Warning, s.Tone);
        }

        [TestMethod]
        public void TruncateFilename_ShortName_Unchanged()
        {
            Assert.AreEqual("hello.png", UploadBatchSummary.TruncateFilename("hello.png"));
        }

        [TestMethod]
        public void TruncateFilename_LongName_TruncatedWithEllipsis()
        {
            var longName = new string('a', 200) + ".png";
            var truncated = UploadBatchSummary.TruncateFilename(longName);

            Assert.AreEqual(60, truncated.Length);
            Assert.IsTrue(truncated.EndsWith('…'));
            Assert.StartsWith(new string('a', 59), truncated);
        }

        [TestMethod]
        public void TruncateFilename_EmptyOrNull_PassesThrough()
        {
            Assert.AreEqual(string.Empty, UploadBatchSummary.TruncateFilename(string.Empty));
            Assert.IsNull(UploadBatchSummary.TruncateFilename(null!));
        }
    }
}
