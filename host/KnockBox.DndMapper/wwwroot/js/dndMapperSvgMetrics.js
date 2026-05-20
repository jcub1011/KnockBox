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
/**
 * Maps a client (CSS) point to SVG user-space coordinates (= grid cells, since
 * the map's viewBox is denominated in cells). Uses the canonical
 * createSVGPoint + getScreenCTM().inverse() pattern, which accounts for the
 * SVG's bounding box, scroll, and any ancestor transforms.
 *
 * Returns null if the element isn't in the DOM or has no CTM yet.
 */
export function clientToSvgPoint(svgId, clientX, clientY) {
    const svg = document.getElementById(svgId);
    if (!svg) return null;
    const ctm = svg.getScreenCTM();
    if (!ctm) return null;
    const pt = svg.createSVGPoint();
    pt.x = clientX;
    pt.y = clientY;
    const p = pt.matrixTransform(ctm.inverse());
    return { x: p.x, y: p.y };
}

export function getViewportMetrics(svgId) {
    const svg = document.getElementById(svgId);
    if (!svg) return null;
    const root = svg.closest('.dnd-mapper-playing');
    if (!root) return null;
    // Temporarily clear the SVG's CSS transform so getBoundingClientRect
    // returns the layout box, not the currently-zoomed/panned visual box.
    // Without this, ResetView reads a shrunk size on each successive press
    // and computes fitZoom against an already-zoomed-out SVG → it keeps
    // zooming further out each call. Matches measureBasePxPerCell's
    // approach in dndMapperViewport.js.
    const prevTransform = svg.style.transform;
    if (prevTransform) svg.style.transform = '';
    const svgRect = svg.getBoundingClientRect();
    if (prevTransform) svg.style.transform = prevTransform;
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
