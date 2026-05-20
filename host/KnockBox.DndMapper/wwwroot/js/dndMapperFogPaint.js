// Fog-paint pointer interop. The .NET caller invokes beginStroke() from its
// mousedown handler when paint/erase mode is active. We attach pointermove +
// pointerup listeners directly to the SVG, accumulate touched cells in a Set,
// and flush them in batches back to .NET via FlushFogStroke. Screen→cell math
// uses the SVG's getScreenCTM (same source as dndMapperSvgMetrics).

const FLUSH_INTERVAL_MS = 150;

let active = null; // { svg, dotnetRef, brushRadius, cells, lastFlush, ctmInverse }

function svgPoint(svg, clientX, clientY) {
    const pt = svg.createSVGPoint();
    pt.x = clientX;
    pt.y = clientY;
    const ctm = svg.getScreenCTM();
    if (!ctm) return null;
    const inv = ctm.inverse();
    return pt.matrixTransform(inv);
}

function paintAt(clientX, clientY) {
    if (!active) return;
    const p = svgPoint(active.svg, clientX, clientY);
    if (!p) return;
    const cx = Math.floor(p.x);
    const cy = Math.floor(p.y);
    // brushRadius = 1 → single cell. brushRadius = N → N×N square centered on cursor.
    const r = active.brushRadius - 1;
    const half = Math.floor(r / 2);
    for (let dy = -half; dy <= r - half; dy++) {
        for (let dx = -half; dx <= r - half; dx++) {
            active.cells.add(`${cx + dx},${cy + dy}`);
        }
    }
}

async function flush() {
    if (!active || active.cells.size === 0) return;
    const xs = [];
    const ys = [];
    for (const key of active.cells) {
        const comma = key.indexOf(",");
        xs.push(parseInt(key.substring(0, comma), 10));
        ys.push(parseInt(key.substring(comma + 1), 10));
    }
    active.cells.clear();
    try {
        await active.dotnetRef.invokeMethodAsync("FlushFogStroke", xs, ys);
    } catch (_) {
        // Disposed component / disconnected circuit — let the next stroke try again.
    }
}

function onMove(ev) {
    if (!active) return;
    ev.preventDefault();
    paintAt(ev.clientX, ev.clientY);
    const now = performance.now();
    if (now - active.lastFlush >= FLUSH_INTERVAL_MS) {
        active.lastFlush = now;
        flush();
    }
}

async function onUp(_ev) {
    if (!active) return;
    const finishing = active;
    finishing.svg.removeEventListener("pointermove", onMove);
    finishing.svg.removeEventListener("pointerup", onUp);
    finishing.svg.removeEventListener("pointercancel", onUp);
    // Flush remaining cells before clearing `active` so flush() sees the buffer.
    await flush();
    if (active === finishing) active = null;
}

export function beginStroke(svgId, dotnetRef, brushRadius, clientX, clientY) {
    const svg = document.getElementById(svgId);
    if (!svg) return;
    // If a stroke was somehow left dangling, tear it down before starting fresh.
    if (active) {
        active.svg.removeEventListener("pointermove", onMove);
        active.svg.removeEventListener("pointerup", onUp);
        active.svg.removeEventListener("pointercancel", onUp);
        active = null;
    }
    active = {
        svg,
        dotnetRef,
        brushRadius: Math.max(1, Math.min(3, brushRadius | 0)),
        cells: new Set(),
        lastFlush: performance.now(),
    };
    paintAt(clientX, clientY);
    flush(); // immediate single-cell paint feels more responsive
    svg.addEventListener("pointermove", onMove);
    svg.addEventListener("pointerup", onUp);
    svg.addEventListener("pointercancel", onUp);
}

export function cancelStroke() {
    if (!active) return;
    active.svg.removeEventListener("pointermove", onMove);
    active.svg.removeEventListener("pointerup", onUp);
    active.svg.removeEventListener("pointercancel", onUp);
    active = null;
}
