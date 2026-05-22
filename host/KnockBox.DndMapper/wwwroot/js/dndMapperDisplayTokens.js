/**
 * DnD Mapper /display token animation — Web Animations API
 *
 * Blazor renders each token's <g> with data-token-id, data-tx, data-ty (and
 * no transform attribute / no inline transform). On every render of the
 * display page, syncTokens() walks those <g>s, compares the data-tx/data-ty
 * to the per-svg cache of last-applied positions, and either:
 *
 *  - seeds the inline `transform` with no animation (new token, or
 *    `instant=true` for the first paint / map swap), or
 *  - calls element.animate(...) with the previous → new positions and a
 *    250 ms ease-out timing.
 *
 * Why this instead of `transition: transform 250ms ease-out` on the SVG
 * attribute: CSS transitions on the SVG `transform` *attribute* are subject
 * to main-thread scheduling jitter; on /display, when the host tab does
 * IDB-save JS interop ~500 ms after a move (same renderer process under
 * same-origin default), the transition gets interrupted mid-flight and
 * the token snaps. Web Animations API on the CSS `transform` property
 * runs on the compositor in Chromium and isn't affected by main-thread
 * blocking the same way.
 *
 * On SVG `<g>`, CSS `transform: translate(Xpx, Ypx)` is interpreted in the
 * parent SVG's user-coordinate system per the CSS Transforms / SVG
 * integration spec, so `Xpx` for X cells is the right unit even though
 * we're nominally in "pixels."
 */

const ANIM_DURATION_MS = 250;
const ANIM_EASING = "ease-out";

// One cache per SVG id. Each maps tokenId → { tx, ty } of the last position
// we applied to that element. Lives at module scope because the /display
// page imports this module once per circuit; the cache survives across
// many renders.
const caches = new Map();

function transformExpr(tx, ty) {
    return `translate(${tx}px, ${ty}px)`;
}

function readNum(el, attr) {
    const raw = el.getAttribute(attr);
    if (raw === null || raw === undefined) return null;
    const n = Number(raw);
    return Number.isFinite(n) ? n : null;
}

/**
 * Reconcile token <g>s under svgId with the cached positions.
 *
 * @param {string} svgId
 * @param {boolean} instant true on first paint or after a map swap; skips
 *                          the WAAPI animation and seeds straight to the
 *                          target position.
 */
export function syncTokens(svgId, instant) {
    const svg = document.getElementById(svgId);
    if (!svg) return;

    let cache = caches.get(svgId);
    if (!cache) {
        cache = new Map();
        caches.set(svgId, cache);
    }

    const seen = new Set();
    const groups = svg.querySelectorAll("[data-token-id]");
    for (const el of groups) {
        const id = el.getAttribute("data-token-id");
        if (!id) continue;
        const tx = readNum(el, "data-tx");
        const ty = readNum(el, "data-ty");
        if (tx === null || ty === null) continue;

        seen.add(id);

        const prev = cache.get(id);
        if (!prev || instant) {
            // First time we've seen this token, or caller asked for a
            // no-animation seed (initial paint / map swap): set the
            // inline transform directly so the next animate() has the
            // right "from" baseline.
            el.style.transform = transformExpr(tx, ty);
            cache.set(id, { tx, ty });
            continue;
        }

        if (prev.tx === tx && prev.ty === ty) continue;

        try {
            el.animate(
                [
                    { transform: transformExpr(prev.tx, prev.ty) },
                    { transform: transformExpr(tx, ty) },
                ],
                {
                    duration: ANIM_DURATION_MS,
                    easing: ANIM_EASING,
                    fill: "forwards",
                }
            );
        } catch {
            // WAAPI failure (very old browser?) — fall back to instant set.
            el.style.transform = transformExpr(tx, ty);
        }
        cache.set(id, { tx, ty });
    }

    // Evict cache entries for tokens that disappeared (deleted, moved off
    // the active map, etc.). Their <g> is gone from the DOM so there's
    // nothing to animate from.
    for (const id of [...cache.keys()]) {
        if (!seen.has(id)) cache.delete(id);
    }
}

/** Drops the per-svg cache so a fresh attach starts clean. */
export function reset(svgId) {
    caches.delete(svgId);
}
