using Markdig;

namespace KnockBox.DndMapperTests.Unit.Helpers
{
    // Regression guard for the Markdig pipeline configured in
    // CharacterSheetPanel.razor.cs. The pipeline is a private static field
    // on the component; rather than expose it, this test rebuilds the same
    // configuration and asserts the safety properties. If the production
    // pipeline drifts from this configuration, the production code's static
    // field needs the same change here.
    [TestClass]
    public class MarkdownPipelineTests
    {
        private static MarkdownPipeline BuildPipeline() =>
            new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .DisableHtml()
                .Build();

        [TestMethod]
        public void DisableHtml_ScriptTag_NotRenderedAsScript()
        {
            var pipeline = BuildPipeline();
            var input = "Notes: <script>alert('xss')</script>";

            var html = Markdown.ToHtml(input, pipeline);

            // The literal <script> tag must not appear in the rendered HTML —
            // Markdig with DisableHtml escapes raw HTML rather than passing it
            // through. The escaped form "&lt;script&gt;" is acceptable.
            StringAssert.DoesNotMatch(html, new System.Text.RegularExpressions.Regex(
                "<script\\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }

        [TestMethod]
        public void DisableHtml_ImageOnerror_NotRenderedAsTag()
        {
            var pipeline = BuildPipeline();
            // Classic stored-XSS payload via raw HTML.
            var input = "<img src=x onerror=\"alert(1)\">";

            var html = Markdown.ToHtml(input, pipeline);

            // The raw <img …> attribute attack vector must be inert.
            StringAssert.DoesNotMatch(html, new System.Text.RegularExpressions.Regex(
                "<img\\b[^>]*onerror", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }

        [TestMethod]
        public void DisableHtml_PlainMarkdown_StillRenders()
        {
            var pipeline = BuildPipeline();

            var html = Markdown.ToHtml("**bold** and *italic*", pipeline);

            StringAssert.Contains(html, "<strong>bold</strong>");
            StringAssert.Contains(html, "<em>italic</em>");
        }

        [TestMethod]
        public void UseAdvancedExtensions_Strikethrough_Renders()
        {
            var pipeline = BuildPipeline();

            var html = Markdown.ToHtml("~~struck~~", pipeline);

            StringAssert.Contains(html, "<del>struck</del>");
        }

        [TestMethod]
        public void UseAdvancedExtensions_Table_Renders()
        {
            var pipeline = BuildPipeline();
            var input = "| a | b |\n|---|---|\n| 1 | 2 |";

            var html = Markdown.ToHtml(input, pipeline);

            StringAssert.Contains(html, "<table");
            StringAssert.Contains(html, "<td>1</td>");
        }
    }
}
