using System.Text.RegularExpressions;
using KnockBox.DndMapper.Helpers;

namespace KnockBox.DndMapperTests.Unit.Helpers
{
    // Safety guard for NotesMarkdownRenderer, which renders character-sheet
    // Notes markdown to HTML for display via MarkupString. Notes are
    // author-editable by players and viewable by other users (incl. the host),
    // so a dangerous note is a cross-user stored-XSS vector. These tests assert
    // both layers of defense: raw-HTML stripping (DisableHtml) and the URL
    // scheme allowlist applied to markdown links/images.
    [TestClass]
    public class MarkdownPipelineTests
    {
        [TestMethod]
        public void DisableHtml_ScriptTag_NotRenderedAsScript()
        {
            var html = NotesMarkdownRenderer.ToSafeHtml("Notes: <script>alert('xss')</script>");

            // The literal <script> tag must not appear in the rendered HTML —
            // DisableHtml escapes raw HTML rather than passing it through.
            StringAssert.DoesNotMatch(html, new Regex("<script\\b", RegexOptions.IgnoreCase));
        }

        [TestMethod]
        public void DisableHtml_ImageOnerror_NotRenderedAsTag()
        {
            var html = NotesMarkdownRenderer.ToSafeHtml("<img src=x onerror=\"alert(1)\">");

            // The raw <img …> attribute attack vector must be inert.
            StringAssert.DoesNotMatch(html, new Regex("<img\\b[^>]*onerror", RegexOptions.IgnoreCase));
        }

        [TestMethod]
        public void Link_JavascriptScheme_Neutralized()
        {
            var html = NotesMarkdownRenderer.ToSafeHtml("[click me](javascript:alert(1))");

            // The anchor still renders, but the dangerous href is stripped.
            StringAssert.Contains(html, "click me");
            StringAssert.DoesNotMatch(html, new Regex("javascript:", RegexOptions.IgnoreCase));
        }

        [TestMethod]
        public void Link_MixedCaseJavascriptScheme_Neutralized()
        {
            // Scheme detection is case-insensitive: a mixed-case scheme that
            // Markdig still parses as a link URL must be neutralized.
            // (Control-char obfuscation like "java\tscript:" is covered by
            // IsSafeUrl_Classification — Markdig doesn't even form a link from
            // it because the control char terminates the URL.)
            var html = NotesMarkdownRenderer.ToSafeHtml("[x](JavaScript:alert(1))");

            StringAssert.DoesNotMatch(html, new Regex("javascript:alert", RegexOptions.IgnoreCase));
        }

        [TestMethod]
        public void Image_DataScheme_Neutralized()
        {
            var html = NotesMarkdownRenderer.ToSafeHtml("![pic](data:text/html,<script>alert(1)</script>)");

            StringAssert.DoesNotMatch(html, new Regex("src=\"data:", RegexOptions.IgnoreCase));
        }

        [TestMethod]
        public void Link_HttpsScheme_Preserved()
        {
            var html = NotesMarkdownRenderer.ToSafeHtml("[docs](https://example.com/page)");

            StringAssert.Contains(html, "https://example.com/page");
        }

        [TestMethod]
        public void Link_RelativeUrl_Preserved()
        {
            var html = NotesMarkdownRenderer.ToSafeHtml("[here](/local/path)");

            StringAssert.Contains(html, "/local/path");
        }

        [TestMethod]
        public void PlainMarkdown_StillRenders()
        {
            var html = NotesMarkdownRenderer.ToSafeHtml("**bold** and *italic*");

            StringAssert.Contains(html, "<strong>bold</strong>");
            StringAssert.Contains(html, "<em>italic</em>");
        }

        [TestMethod]
        public void AdvancedExtensions_Strikethrough_Renders()
        {
            var html = NotesMarkdownRenderer.ToSafeHtml("~~struck~~");

            StringAssert.Contains(html, "<del>struck</del>");
        }

        [TestMethod]
        public void AdvancedExtensions_Table_Renders()
        {
            var html = NotesMarkdownRenderer.ToSafeHtml("| a | b |\n|---|---|\n| 1 | 2 |");

            StringAssert.Contains(html, "<table");
            StringAssert.Contains(html, "<td>1</td>");
        }

        [TestMethod]
        public void IsSafeUrl_Classification()
        {
            Assert.IsTrue(NotesMarkdownRenderer.IsSafeUrl(null));
            Assert.IsTrue(NotesMarkdownRenderer.IsSafeUrl(""));
            Assert.IsTrue(NotesMarkdownRenderer.IsSafeUrl("/relative/path"));
            Assert.IsTrue(NotesMarkdownRenderer.IsSafeUrl("#anchor"));
            Assert.IsTrue(NotesMarkdownRenderer.IsSafeUrl("page.html?x=1"));
            Assert.IsTrue(NotesMarkdownRenderer.IsSafeUrl("https://example.com"));
            Assert.IsTrue(NotesMarkdownRenderer.IsSafeUrl("HTTP://EXAMPLE.COM"));
            Assert.IsTrue(NotesMarkdownRenderer.IsSafeUrl("mailto:a@b.com"));

            Assert.IsFalse(NotesMarkdownRenderer.IsSafeUrl("javascript:alert(1)"));
            Assert.IsFalse(NotesMarkdownRenderer.IsSafeUrl("data:text/html,x"));
            Assert.IsFalse(NotesMarkdownRenderer.IsSafeUrl("vbscript:msgbox"));
            Assert.IsFalse(NotesMarkdownRenderer.IsSafeUrl(" java\tscript:alert(1)"));
        }
    }
}
