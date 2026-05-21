// Focus-box drag interop. While focus mode is active the host drags out a
// rectangle that becomes the display view's viewBox. Mirrors
// dndMapperFogPaint's shape: the preview is a client-only <g> appended to
// the SVG outside the Razor render tree, per-frame updates are pure DOM
// mutations, and .NET is invoked once at pointer-up via CommitFocusRect.

import { clientToSvgPoint } from "./dndMapperSvgMetrics.js";

const SVG_NS = "http://www.w3.org/2000/svg";

let active = null;

function normalize(start, current, snapToGrid) {
    let x0 = Math.min(start.x, current.x);
    let y0 = Math.min(start.y, current.y);
    let x1 = Math.max(start.x, current.x);
    let y1 = Math.max(start.y, current.y);
    if (snapToGrid) {
        x0 = Math.floor(x0);
        y0 = Math.floor(y0);
        x1 = Math.ceil(x1);
        y1 = Math.ceil(y1);
    }
    return { x: x0, y: y0, w: x1 - x0, h: y1 - y0 };
}

function applyRect(rect, x, y, w, h) {
    rect.setAttribute("x", x);
    rect.setAttribute("y", y);
    rect.setAttribute("width", w);
    rect.setAttribute("height", h);
}

function paint(state) {
    const n = normalize(state.start, state.current, state.snapToGrid);
    applyRect(state.halo, n.x, n.y, n.w, n.h);
    applyRect(state.dashed, n.x, n.y, n.w, n.h);
    return n;
}

function detach(state) {
    state.svg.removeEventListener("pointermove", onMove);
    state.svg.removeEventListener("pointerup", onUp);
    state.svg.removeEventListener("pointercancel", onCancel);
}

function removePreview(state) {
    if (state.preview && state.preview.parentNode) {
        state.preview.parentNode.removeChild(state.preview);
    }
}

function onMove(ev) {
    if (!active) return;
    ev.preventDefault();
    const pt = clientToSvgPoint(active.svgId, ev.clientX, ev.clientY);
    if (!pt) return;
    active.current = { x: pt.x, y: pt.y };
    paint(active);
}

async function onUp(_ev) {
    if (!active) return;
    const finishing = active;
    detach(finishing);
    const n = normalize(finishing.start, finishing.current, finishing.snapToGrid);
    removePreview(finishing);
    if (active === finishing) active = null;

    if (n.w <= 0 || n.h <= 0) return;
    try {
        await finishing.dotnetRef.invokeMethodAsync("CommitFocusRect", n.x, n.y, n.w, n.h);
    } catch (_) {
        // Component disposed / circuit disconnected. The preview is already
        // gone; the server-authoritative focus rect (if it commits later via
        // some other path) will be drawn by the Razor template as usual.
    }
}

function onCancel(_ev) {
    if (!active) return;
    const finishing = active;
    detach(finishing);
    removePreview(finishing);
    if (active === finishing) active = null;
}

export function beginDrag(svgId, dotnetRef, snapToGrid, clientX, clientY) {
    const svg = document.getElementById(svgId);
    if (!svg) return;
    if (active) {
        detach(active);
        removePreview(active);
        active = null;
    }
    const startPt = clientToSvgPoint(svgId, clientX, clientY);
    if (!startPt) return;

    const preview = document.createElementNS(SVG_NS, "g");
    preview.setAttribute("class", "dndm-focus-preview");
    preview.setAttribute("pointer-events", "none");

    // Mirrors the Razor template attributes: black halo underlay (visible
    // against red/bright backgrounds) plus the dashed red preview stroke.
    const halo = document.createElementNS(SVG_NS, "rect");
    halo.setAttribute("fill", "none");
    halo.setAttribute("stroke", "#000");
    halo.setAttribute("stroke-width", "0.18");
    halo.setAttribute("stroke-opacity", "0.6");
    const dashed = document.createElementNS(SVG_NS, "rect");
    dashed.setAttribute("class", "dndm-focus-rect-preview");
    dashed.setAttribute("fill", "none");
    dashed.setAttribute("stroke", "#e11d48");
    dashed.setAttribute("stroke-width", "0.12");
    dashed.setAttribute("stroke-dasharray", "0.4,0.25");
    preview.appendChild(halo);
    preview.appendChild(dashed);
    svg.appendChild(preview);

    active = {
        svg,
        svgId,
        dotnetRef,
        snapToGrid: !!snapToGrid,
        start: { x: startPt.x, y: startPt.y },
        current: { x: startPt.x, y: startPt.y },
        preview,
        halo,
        dashed,
    };
    paint(active);

    svg.addEventListener("pointermove", onMove);
    svg.addEventListener("pointerup", onUp);
    svg.addEventListener("pointercancel", onCancel);
}

export function cancelDrag() {
    if (!active) return;
    detach(active);
    removePreview(active);
    active = null;
}
