// Fog-paint pointer interop. The .NET caller invokes beginStroke() from its
// mousedown handler when paint/erase mode is active. We attach pointermove +
// pointerup directly to the SVG, render a client-only preview <g> appended
// to the SVG (invisible to Blazor's diff because we created it outside the
// Razor render tree), and only call back into .NET once — at pointerup —
// with the complete cell list. That keeps the stroke fluid even when the
// SignalR round-trip is slow; the server pushes the authoritative fog state
// back via the usual state-change render after the verb returns.

import { clientToSvgPoint } from "./dndMapperSvgMetrics.js";

const SVG_NS = "http://www.w3.org/2000/svg";

// Paint preview matches the host's final fog overlay opacity so the host sees
// roughly the same coverage they'll get once the verb commits. Erase uses an
// ember tint so the host has unambiguous "these will be revealed" feedback
// (otherwise erasing over already-fogged cells would look the same as paint).
const PAINT_FILL = "#000";
const PAINT_OPACITY = "0.45";
const ERASE_FILL = "#e89055";
const ERASE_OPACITY = "0.35";

let active = null;

function paintAt(clientX, clientY) {
    if (!active) return;
    const p = clientToSvgPoint(active.svgId, clientX, clientY);
    if (!p) return;
    const cell = { cx: Math.floor(p.x), cy: Math.floor(p.y) };
    const r = active.brushRadius - 1;
    const half = Math.floor(r / 2);
    const fill = active.mode === "paint" ? PAINT_FILL : ERASE_FILL;
    const opacity = active.mode === "paint" ? PAINT_OPACITY : ERASE_OPACITY;
    for (let dy = -half; dy <= r - half; dy++) {
        for (let dx = -half; dx <= r - half; dx++) {
            const cx = cell.cx + dx;
            const cy = cell.cy + dy;
            const key = `${cx},${cy}`;
            if (active.cells.has(key)) continue;
            active.cells.add(key);
            const rect = document.createElementNS(SVG_NS, "rect");
            rect.setAttribute("x", cx);
            rect.setAttribute("y", cy);
            rect.setAttribute("width", "1");
            rect.setAttribute("height", "1");
            rect.setAttribute("fill", fill);
            rect.setAttribute("fill-opacity", opacity);
            active.preview.appendChild(rect);
        }
    }
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
    paintAt(ev.clientX, ev.clientY);
}

async function onUp(_ev) {
    if (!active) return;
    const finishing = active;
    detach(finishing);

    if (finishing.cells.size === 0) {
        removePreview(finishing);
        if (active === finishing) active = null;
        return;
    }

    const xs = [];
    const ys = [];
    for (const key of finishing.cells) {
        const comma = key.indexOf(",");
        xs.push(parseInt(key.substring(0, comma), 10));
        ys.push(parseInt(key.substring(comma + 1), 10));
    }

    try {
        await finishing.dotnetRef.invokeMethodAsync(
            "ApplyFogStroke", xs, ys, finishing.mode === "paint");
    } catch (_) {
        // Component disposed / circuit disconnected. The preview is removed
        // either way so the host doesn't see stale client-only paint.
    }
    // Remove the preview only after the server returned; by then the
    // state-change notification has propagated and Blazor has scheduled a
    // re-render of the authoritative fog overlay. There is at most a 1-frame
    // gap; in practice not noticeable.
    removePreview(finishing);
    if (active === finishing) active = null;
}

function onCancel(_ev) {
    if (!active) return;
    const finishing = active;
    detach(finishing);
    removePreview(finishing);
    if (active === finishing) active = null;
}

export function beginStroke(svgId, dotnetRef, brushRadius, mode, clientX, clientY) {
    const svg = document.getElementById(svgId);
    if (!svg) return;
    if (active) {
        detach(active);
        removePreview(active);
        active = null;
    }

    const preview = document.createElementNS(SVG_NS, "g");
    preview.setAttribute("class", "dndm-fog-preview");
    preview.setAttribute("pointer-events", "none");
    svg.appendChild(preview);

    active = {
        svg,
        svgId,
        dotnetRef,
        brushRadius: Math.max(1, Math.min(3, brushRadius | 0)),
        mode: mode === "paint" ? "paint" : "erase",
        cells: new Set(),
        preview,
    };
    paintAt(clientX, clientY);
    svg.addEventListener("pointermove", onMove);
    svg.addEventListener("pointerup", onUp);
    svg.addEventListener("pointercancel", onCancel);
}

export function cancelStroke() {
    if (!active) return;
    detach(active);
    removePreview(active);
    active = null;
}
