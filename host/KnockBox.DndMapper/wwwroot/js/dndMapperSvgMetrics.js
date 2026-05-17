/**
 * Returns how many client (CSS) pixels currently map to one SVG user-space unit
 * (= one grid cell, since the map's viewBox is denominated in cells). Uses
 * getScreenCTM().a, which is the X-axis scale of the SVG's current transform.
 * With xMidYMid meet preserveAspectRatio the Y-scale is identical, so the
 * single value works for both axes.
 *
 * Returns 0 if the element isn't in the DOM or has no CTM yet (caller should
 * fall back to its own estimate).
 */
export function getPixelsPerCell(svgId) {
    const svg = document.getElementById(svgId);
    if (!svg) return 0;
    const ctm = svg.getScreenCTM();
    return ctm ? ctm.a : 0;
}

/**
 * Returns the SVG element's pixel dimensions along with the current widths
 * of the playing-phase rails. Used by ResetView/fit-to-viewport so the map
 * is centered within the visible area (not the full canvas, since rails
 * overlay the canvas with their own opaque content).
 *
 * Returns null if the element isn't in the DOM or the playing root can't
 * be located.
 */
export function getViewportMetrics(svgId) {
    const svg = document.getElementById(svgId);
    if (!svg) return null;
    const root = svg.closest('.dnd-mapper-playing');
    if (!root) return null;
    const svgRect = svg.getBoundingClientRect();
    const leftEl = root.querySelector('.dndm-rail--left');
    const rightEl = root.querySelector('.dndm-rail--right');
    const leftPx = leftEl ? leftEl.getBoundingClientRect().width : 0;
    const rightPx = rightEl ? rightEl.getBoundingClientRect().width : 0;
    return {
        svgWidth: svgRect.width,
        svgHeight: svgRect.height,
        leftPx,
        rightPx,
    };
}
