using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Helpers
{
    internal static class TokenIcons
    {
        // Minimal eye / eye-slash SVG. `visible=true` renders an open eye;
        // `false` renders an eye with a diagonal slash. Used by the token
        // panel (token + initial visibility) and the layer panel (layer
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
    }
}
