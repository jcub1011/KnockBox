using System.IO;
using System.Linq;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace KnockBox.DndMapper.Helpers
{
    // Renders character-sheet Notes markdown to HTML for display via MarkupString.
    //
    // Two layers of defense, because Notes are author-editable by players
    // (canEdit) and viewable by other users including the host (canSeePrivate),
    // so a malicious note is a cross-user stored-XSS vector:
    //   1. DisableHtml() — strips raw HTML (<script>, <img onerror>, …) so the
    //      author can't inject tags.
    //   2. URL scheme allowlist — Markdig still renders markdown links/images
    //      like [x](javascript:alert(1)) as a live anchor/img. DisableHtml does
    //      NOT touch those, so we walk the parsed document and neutralize any
    //      LinkInline whose URL scheme isn't relative or in the allowlist.
    public static class NotesMarkdownRenderer
    {
        // UseAdvancedExtensions gives tables/strikethrough/etc.; DisableHtml
        // strips raw HTML. Built once per process.
        private static readonly MarkdownPipeline Pipeline =
            new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .DisableHtml()
                .Build();

        public static string ToSafeHtml(string? markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return string.Empty;

            var document = Markdown.Parse(markdown, Pipeline);
            foreach (var link in document.Descendants<LinkInline>())
            {
                if (!IsSafeUrl(link.Url))
                {
                    // Render the link text/image but with an inert href/src so
                    // the dangerous scheme can never execute on click.
                    link.Url = string.Empty;
                }
            }

            using var writer = new StringWriter();
            var renderer = new HtmlRenderer(writer);
            Pipeline.Setup(renderer);
            renderer.Render(document);
            writer.Flush();
            return writer.ToString();
        }

        // Safe = relative/anchor (no scheme) or an allowlisted scheme. Control
        // characters and spaces are stripped before scheme detection so a
        // "java\tscript:" style payload — which browsers resolve to javascript:
        // — can't sneak past the prefix check.
        internal static bool IsSafeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return true;

            var cleaned = new string(url.Where(c => !char.IsControl(c) && c != ' ').ToArray());
            var colon = cleaned.IndexOf(':');
            if (colon < 0) return true; // no scheme → relative path or anchor

            // A '/', '#' or '?' before the first ':' means the ':' belongs to a
            // path/query/fragment, not a scheme (e.g. "page#a:b") → relative.
            var delimiter = cleaned.IndexOfAny(['/', '#', '?']);
            if (delimiter >= 0 && delimiter < colon) return true;

            var scheme = cleaned[..colon].ToLowerInvariant();
            return scheme is "http" or "https" or "mailto";
        }
    }
}
