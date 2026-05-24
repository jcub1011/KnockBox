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

/**
 * Returns the canvas-stage rect, the current widths of the playing-phase
 * rails, and the rail-aware visible-center anchor (in CSS px, measured from
 * the stage's top-left). One source of truth for any code that needs to
 * know "where the user is actually looking" — the visible centre after the
 * left/right rails overlay their portion of the stage.
 *
 * Returns null if the element isn't in the DOM or the playing root / stage
 * can't be located yet. Callers MUST handle null (in particular, do not
 * fall back silently to (0,0) — that's the stage's top-left, not its
 * centre, and silently using it has caused token-spawn-off-target bugs).
 *
 * Used by:
 *   - dndMapperViewport.js (wheel zoom anchor, +/- zoom button anchor,
 *     commit-time visible-center publish, centerOnWorld).
 *   - getViewportMetrics (fit-to-viewport — only needs the rail widths).
 *   - any future component that needs to position UI relative to the
 *     visible non-rail centre.
 */
export function getStageAnchor(svgId) {
    const svg = document.getElementById(svgId);
    if (!svg) return null;
    const root = svg.closest('.dnd-mapper-playing');
    if (!root) return null;
    const stage = svg.closest('.dndm-canvas-stage');
    if (!stage) return null;
    const stageRect = stage.getBoundingClientRect();
    const leftEl = root.querySelector('.dndm-rail--left');
    const rightEl = root.querySelector('.dndm-rail--right');
    const leftPx = leftEl ? leftEl.getBoundingClientRect().width : 0;
    const rightPx = rightEl ? rightEl.getBoundingClientRect().width : 0;
    return {
        stageRect,
        leftPx,
        rightPx,
        anchorX: (stageRect.width + leftPx - rightPx) / 2,
        anchorY: stageRect.height / 2,
    };
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
    const anchor = getStageAnchor(svgId);
    if (!anchor) {
        // Fallback path: stage couldn't be resolved (e.g. mid-teardown). Use
        // the SVG's own bounding rect so callers still get plausible dims.
        const svg = document.getElementById(svgId);
        if (!svg) return null;
        const r = svg.getBoundingClientRect();
        return { svgWidth: r.width, svgHeight: r.height, leftPx: 0, rightPx: 0 };
    }
    return {
        svgWidth: anchor.stageRect.width,
        svgHeight: anchor.stageRect.height,
        leftPx: anchor.leftPx,
        rightPx: anchor.rightPx,
    };
}
