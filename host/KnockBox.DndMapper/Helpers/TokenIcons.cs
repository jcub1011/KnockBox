using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Helpers
{
    internal static class TokenIcons
    {
        // Minimal eye / eye-slash SVG. `visible=true` renders an open eye;
        // `false` renders an eye with a diagonal slash. Used for *map*
        // visibility toggles (token panel: Hidden; layer panel: layer
        // visibility) so the iconography stays consistent.
        public static MarkupString Eye(bool visible) => new(visible
            ? "<svg viewBox=\"0 0 16 16\" width=\"14\" height=\"14\" aria-hidden=\"true\" focusable=\"false\">" +
              "<path fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.4\" stroke-linecap=\"round\" stroke-linejoin=\"round\" " +
              "d=\"M1 8s2.5-4.5 7-4.5S15 8 15 8s-2.5 4.5-7 4.5S1 8 1 8z\"/>" +
              "<circle cx=\"8\" cy=\"8\" r=\"2\" fill=\"currentColor\"/></svg>"
            : "<svg viewBox=\"0 0 16 16\" width=\"14\" height=\"14\" aria-hidden=\"true\" focusable=\"false\">" +
              "<path fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.4\" stroke-linecap=\"round\" stroke-linejoin=\"round\" " +
              "d=\"M1 8s2.5-4.5 7-4.5S15 8 15 8s-2.5 4.5-7 4.5S1 8 1 8z\"/>" +
              "<circle cx=\"8\" cy=\"8\" r=\"2\" fill=\"currentColor\"/>" +
              "<line x1=\"2\" y1=\"14\" x2=\"14\" y2=\"2\" stroke=\"currentColor\" stroke-width=\"1.4\" stroke-linecap=\"round\"/></svg>");

        // Token-glyph style indicator. Mirrors what the token *itself* looks
        // like on the map: a circle with "A" inside when IconKind.Initial,
        // or a solid filled circle when IconKind.Solid. Distinct from Eye()
        // so the two visibility-style toggles in the token panel — letter
        // shown vs token shown — don't render the same glyph.
        public static MarkupString Glyph(bool showInitial) => new(showInitial
            ? "<svg viewBox=\"0 0 16 16\" width=\"14\" height=\"14\" aria-hidden=\"true\" focusable=\"false\">" +
              "<circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.4\"/>" +
              "<text x=\"8\" y=\"8\" text-anchor=\"middle\" dominant-baseline=\"central\" " +
              "font-family=\"Georgia, serif\" font-size=\"8\" font-weight=\"700\" fill=\"currentColor\">A</text></svg>"
            : "<svg viewBox=\"0 0 16 16\" width=\"14\" height=\"14\" aria-hidden=\"true\" focusable=\"false\">" +
              "<circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"currentColor\" stroke=\"currentColor\" stroke-width=\"1.4\"/></svg>");
    }
}
