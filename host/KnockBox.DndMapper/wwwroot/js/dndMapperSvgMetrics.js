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
