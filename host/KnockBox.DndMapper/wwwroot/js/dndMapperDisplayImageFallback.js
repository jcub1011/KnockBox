// DnD Mapper /display image fallback.
//
// The display view renders one SVG <image href="/blob-share/{token}"> per
// map image. If the endpoint returns 4xx/5xx — e.g. the originating host
// circuit briefly stalled and the per-stream watchdog tripped (returning
// 503) — the browser would otherwise render the broken-image glyph. This
// handler swaps the href to a local placeholder asset and logs the token
// so the host can correlate with the server-side LogError line.
//
// Defined on `window` (rather than as an ES module export) because the
// SVG onerror attribute runs in the global scope and can't `await import`.
(function () {
    if (window.__dndDisplayImageError) return;

    const PLACEHOLDER = "/_content/KnockBox.DndMapper/img/blob-missing.svg";

    window.__dndDisplayImageError = function (el, token) {
        if (!el) return;
        // Guard against the placeholder itself failing — without this an
        // infinite onerror loop would hammer the console.
        if (el.dataset.fallbackApplied === "1") return;
        el.dataset.fallbackApplied = "1";

        try {
            el.setAttribute("href", PLACEHOLDER);
        } catch (e) {
            // Defensive: setAttribute can't realistically throw on a live
            // SVGImageElement, but log if it ever does so the diagnostic
            // path stays visible.
            console.error("DnD Mapper: placeholder swap failed for blob-share", token, e);
        }

        console.warn(
            "DnD Mapper: blob-share image failed to load; showing placeholder.",
            { token, href: el.getAttribute("href") });
    };
})();
