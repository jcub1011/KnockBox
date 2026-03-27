using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace KnockBox.Core.Services.Drawing
{
    /// <summary>
    /// Server-side mirror of the JavaScript SVG stroke sanitizer defined in
    /// <c>svgDrawingCanvas.js</c>. Strips any SVG elements or attributes that are not
    /// in the allowlist so that client-submitted SVG markup cannot be used for XSS
    /// attacks when rendered via <see cref="Microsoft.AspNetCore.Components.MarkupString"/>.
    ///
    /// Allowlist mirrors <c>ALLOWED_STROKE_TAGS</c> and <c>ALLOWED_STROKE_ATTRS</c> in
    /// <c>svgDrawingCanvas.js</c>:
    /// <list type="bullet">
    ///   <item>Elements: <c>path</c>, <c>circle</c></item>
    ///   <item>Attributes: <c>d</c>, <c>stroke</c>, <c>stroke-width</c>, <c>fill</c>,
    ///     <c>stroke-linecap</c>, <c>stroke-linejoin</c>, <c>cx</c>, <c>cy</c>, <c>r</c>
    ///   </item>
    /// </list>
    /// </summary>
    public static class SvgContentSanitizer
    {
        private static readonly XmlReaderSettings XmlReaderSettings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersFromEntities = 0,
            XmlResolver = null,
        };

        /// <summary>
        /// 1024 kb
        /// </summary>
        private const int MAX_SVG_CONTENT_LENGTH = 1024 * 1024;

        // Mirrors ALLOWED_STROKE_TAGS in svgDrawingCanvas.js
        private static readonly HashSet<string> AllowedTags = ["path", "circle", "g"];

        // Tags that are containers (have children rather than self-closing)
        private static readonly HashSet<string> ContainerTags = ["g"];

        // Mirrors ALLOWED_STROKE_ATTRS in svgDrawingCanvas.js
        private static readonly HashSet<string> AllowedAttrs =
        [
            "d", "stroke", "stroke-width", "fill",
            "stroke-linecap", "stroke-linejoin", "cx", "cy", "r",
            "transform",
        ];

        /// <summary>
        /// Sanitizes SVG inner markup (strokes only, no <c>&lt;svg&gt;</c> wrapper) by
        /// retaining only <c>path</c> and <c>circle</c> elements with the allowed
        /// attribute set. Returns <see langword="null"/> when <paramref name="svgContent"/>
        /// is null, empty, or contains no valid strokes after sanitization.
        /// </summary>
        public static string? Sanitize(string? svgContent)
        {
            if (string.IsNullOrWhiteSpace(svgContent)) return null;
            if (svgContent.Length > MAX_SVG_CONTENT_LENGTH) return null;

            try
            {
                using var reader = XmlReader.Create(
                    new StringReader($"<svg xmlns='http://www.w3.org/2000/svg'>{svgContent}</svg>"), 
                    XmlReaderSettings);

                // Wrap inner markup in a temporary <svg> root for parsing.
                var doc = XDocument.Load(reader, LoadOptions.None);

                var sb = new StringBuilder();
                SanitizeElements(doc.Root!.Elements(), sb);
                return sb.Length > 0 ? sb.ToString() : null;
            }
            catch
            {
                // Unparseable content is discarded entirely.
                return null;
            }
        }

        private static void SanitizeElements(IEnumerable<XElement> elements, StringBuilder sb, int depth = 0)
        {
            if (depth++ > 32) return;

            foreach (var el in elements)
            {
                var tag = el.Name.LocalName.ToLowerInvariant();
                if (!AllowedTags.Contains(tag)) continue;

                sb.Append('<').Append(tag);

                foreach (var attr in el.Attributes())
                {
                    var attrName = attr.Name.LocalName.ToLowerInvariant();
                    if (!AllowedAttrs.Contains(attrName)) continue;
                    sb.Append(' ')
                      .Append(attrName)
                      .Append("=\"")
                      .Append(EscapeXmlAttrValue(attr.Value))
                      .Append('"');
                }

                if (ContainerTags.Contains(tag))
                {
                    sb.Append('>');
                    SanitizeElements(el.Elements(), sb, depth);
                    sb.Append("</").Append(tag).Append('>');
                }
                else
                {
                    sb.Append("/>");
                }
            }
        }

        // Escapes the five XML special characters that can appear in attribute values.
        private static string EscapeXmlAttrValue(string value)
            => value
                .Replace("&", "&amp;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
    }
}
