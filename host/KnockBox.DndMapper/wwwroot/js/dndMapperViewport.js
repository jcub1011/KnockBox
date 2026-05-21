/**
 * DnD Mapper Viewport — JS-owned pan and zoom for the MapCanvas.
 *
 * All pan and zoom (canonical state plus any in-flight gesture delta) is
 * realized as a CSS `transform: translate3d(...) scale(...)` on the
 * `.dndm-canvas-transform` wrapper. The wrapper contains two siblings — an
 * HTML bitmap layer (one <img> per map image) and the SVG vector layer —
 * both naturally sized to W*cellPx × H*cellPx CSS px. Transforming the
 * wrapper composites both layers as a single GPU operation, with each
 * <img> riding on its own GPU texture inside.
 *
 * Off-map content (images placed past the map edge) renders correctly via
 * the SVG's `overflow="visible"` attribute. The parent .dndm-canvas-stage
 * clips with CSS overflow:hidden so off-map content can't escape into the
 * toolbar or rails.
 */

const instances = new Map();

const MIN_ZOOM = 0.01;
const MAX_ZOOM = 10.0;
const CLICK_DEAD_ZONE_PX = 3;
const WHEEL_COMMIT_DEBOUNCE_MS = 140;

function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }

const BAIL_SELECTOR = [
    '[data-token-id]',
    '.dndm-image-pickers rect',
    '.dndm-image-handle',
    '.dndm-focus-rect-preview',
    '.dndm-fog-preview',
].join(',');

function targetWantsGesture(target) {
    if (!(target instanceof Element)) return false;
    return target.closest(BAIL_SELECTOR) !== null;
}

// Pixels-per-cell is now a static property of the map — the wrapper and SVG
// are naturally sized to W*cellPx × H*cellPx, so 1 cell = cellPx CSS px
// regardless of zoom. The pan/zoom transform on the wrapper scales this
// natural size on the GPU. Kept as a function for callsite parity.
function ensureBasePxPerCell(state) {
    return state.basePxPerCell;
}

// Combined canonical+gesture viewport in map cell coordinates. Mirrors the
// legacy viewBox-mutation math so visual results match exactly.
function combinedViewport(state) {
    const factor = state.gZoomFactor;
    const combinedZoom = clamp(state.zoom * factor, MIN_ZOOM, MAX_ZOOM);
    const effectiveFactor = state.zoom > 0 ? combinedZoom / state.zoom : 1.0;

    const oldVbW = state.widthCells / state.zoom;
    const oldVbH = state.heightCells / state.zoom;
    const newVbW = state.widthCells / combinedZoom;
    const newVbH = state.heightCells / combinedZoom;

    const startPxPerCell = state.basePxPerCell > 0 ? state.basePxPerCell * state.zoom : 1;
    const effectivePxPerCell = startPxPerCell * effectiveFactor;

    const combinedPanX = state.panX + (oldVbW - newVbW) / 2 - state.gPanPx.x / effectivePxPerCell;
    const combinedPanY = state.panY + (oldVbH - newVbH) / 2 - state.gPanPx.y / effectivePxPerCell;

    return { panX: combinedPanX, panY: combinedPanY, zoom: combinedZoom };
}

function applyTransform(state) {
    const base = ensureBasePxPerCell(state);
    if (base <= 0) return;
    const { panX, panY, zoom } = combinedViewport(state);
    const W = state.widthCells;
    const H = state.heightCells;
    const tx = base * (W * (zoom - 1) / 2 - zoom * panX);
    const ty = base * (H * (zoom - 1) / 2 - zoom * panY);
    state.wrapper.style.transform = `translate3d(${tx}px, ${ty}px, 0) scale(${zoom})`;
}

// Zoom-with-anchor: keep the world coord under `anchorStageX/Y` (CSS px,
// measured from the gesture-surface's top-left) at the same stage pixel
// position before and after the zoom. Used by the wheel handler (anchor =
// cursor) and the toolbar +/- buttons (anchor = visible non-rail centre).
// Commits any in-flight gesture state into the canonical pan/zoom first,
// then writes the post-zoom pan back to canonical state and reapplies the
// transform. Returns true if zoom changed.
function applyZoomAtAnchor(state, anchorStageX, anchorStageY, factor) {
    const base = ensureBasePxPerCell(state);
    if (base <= 0) return false;
    // Fold any pending gesture into canonical pan/zoom so the anchor math
    // operates against a clean baseline. After this block the gesture
    // identity is restored.
    const combined = combinedViewport(state);
    state.panX = combined.panX;
    state.panY = combined.panY;
    state.zoom = combined.zoom;
    state.gPanPx = { x: 0, y: 0 };
    state.gZoomFactor = 1.0;

    const newZoom = clamp(state.zoom * factor, MIN_ZOOM, MAX_ZOOM);
    if (newZoom === state.zoom) return false;

    // World coord under the anchor at the current zoom: panX is the world
    // x of the stage's left edge (per the inverse of applyTransform's
    // tx formula), so the anchor world x is panX + anchorStageX/(cellPx*zoom).
    const worldX = state.panX + anchorStageX / (base * state.zoom);
    const worldY = state.panY + anchorStageY / (base * state.zoom);
    // Pin that world coord back under the anchor after the zoom change.
    state.panX = worldX - anchorStageX / (base * newZoom);
    state.panY = worldY - anchorStageY / (base * newZoom);
    state.zoom = newZoom;

    applyTransform(state);
    return true;
}

function refreshLayer(state) {
    if (!state.wrapper) return;
    state.wrapper.style.willChange = 'auto';
    void state.wrapper.offsetWidth;
    state.wrapper.style.willChange = 'transform';
}

function scheduleLayerRefresh(state) {
    if (state.refreshScheduled) return;
    state.refreshScheduled = true;
    requestAnimationFrame(() => {
        state.refreshScheduled = false;
        if (state.pan || state.wheelDebounceHandle) {
            state.refreshAfterCommit = true;
            return;
        }
        refreshLayer(state);
    });
}

function commitGesture(state, wasClickWithoutDrag) {
    if (state.wheelDebounceHandle) {
        clearTimeout(state.wheelDebounceHandle);
        state.wheelDebounceHandle = 0;
    }
    const combined = combinedViewport(state);
    state.panX = combined.panX;
    state.panY = combined.panY;
    state.zoom = combined.zoom;
    state.gPanPx = { x: 0, y: 0 };
    state.gZoomFactor = 1.0;
    applyTransform(state);
    if (state.refreshAfterCommit) {
        state.refreshAfterCommit = false;
        requestAnimationFrame(() => {
            if (state.pan || state.wheelDebounceHandle) {
                state.refreshAfterCommit = true;
                return;
            }
            refreshLayer(state);
        });
    }
    if (state.dotNetRef) {
        state.dotNetRef.invokeMethodAsync(
            'OnViewportChanged', state.panX, state.panY, state.zoom, wasClickWithoutDrag)
            .catch(err => console.error('[DndMapperViewport] OnViewportChanged failed.', err));
    }
}

export function initialize(svgId, dotNetRef, panX, panY, zoom, widthCells, heightCells, cellPx, initialMode) {
    const svg = document.getElementById(svgId);
    if (!svg) {
        console.error(`[DndMapperViewport] initialize: element "${svgId}" not found.`);
        return;
    }

    dispose(svgId);

    // Resolve the transform wrapper that holds the bitmap layer and the SVG.
    // dndMapperViewport.js writes its pan/zoom transform onto the wrapper so
    // both layers ride one GPU-composited transform.
    const wrapper = svg.closest('.dndm-canvas-transform');
    if (!wrapper) {
        console.error(`[DndMapperViewport] initialize: .dndm-canvas-transform ancestor for "${svgId}" not found.`);
        return;
    }

    // Gestures fire on the entire canvas-stage so pan/zoom works anywhere in
    // the visible viewport — including the centering padding around the
    // naturally-sized wrapper. Token-drag still listens on the SVG and
    // stopPropagation()s its events, so the two coexist.
    const gestureSurface = svg.closest('.dndm-canvas-stage') || wrapper;

    const abortController = new AbortController();
    const signal = abortController.signal;

    const state = {
        svg,
        wrapper,
        gestureSurface,
        dotNetRef,
        abortController,
        widthCells,
        heightCells,
        panX,
        panY,
        zoom,
        mode: initialMode || 'none',
        gPanPx: { x: 0, y: 0 },
        gZoomFactor: 1.0,
        basePxPerCell: cellPx > 0 ? cellPx : 0,
        pan: null,
        wheelDebounceHandle: 0,
        refreshScheduled: false,
        refreshAfterCommit: false,
    };
    instances.set(svgId, state);

    applyTransform(state);

    function scheduleWheelCommit() {
        if (state.wheelDebounceHandle) clearTimeout(state.wheelDebounceHandle);
        state.wheelDebounceHandle = setTimeout(() => {
            state.wheelDebounceHandle = 0;
            if (state.pan) return;
            commitGesture(state, false);
        }, WHEEL_COMMIT_DEBOUNCE_MS);
    }

    gestureSurface.addEventListener('wheel', (e) => {
        e.preventDefault();
        // Cursor-anchored zoom: keep the world point under the cursor at the
        // same stage pixel position before and after the zoom. The previous
        // formula preserved the WORLD coord at the wrapper's geometric centre,
        // which lives at the stage's top-left (not the visible viewport's
        // centre) — so as you zoomed, the visible centre drifted left.
        const factor = e.deltaY < 0 ? 1.1 : 1.0 / 1.1;
        const stageRect = state.gestureSurface.getBoundingClientRect();
        const cursorStageX = e.clientX - stageRect.left;
        const cursorStageY = e.clientY - stageRect.top;
        if (applyZoomAtAnchor(state, cursorStageX, cursorStageY, factor)) {
            scheduleWheelCommit();
        }
    }, { passive: false, signal });

    gestureSurface.addEventListener('mousedown', (e) => {
        if (state.pan) return;
        if (e.button !== 0 && e.button !== 1) return;
        if (targetWantsGesture(e.target)) return;
        if (e.button === 0 && state.mode !== 'none') return;
        ensureBasePxPerCell(state);
        state.pan = {
            button: e.button,
            startClientX: e.clientX,
            startClientY: e.clientY,
            lastClientX: e.clientX,
            lastClientY: e.clientY,
            moved: false,
        };
    }, { signal });

    gestureSurface.addEventListener('mousemove', (e) => {
        if (!state.pan) return;
        const dx = e.clientX - state.pan.lastClientX;
        const dy = e.clientY - state.pan.lastClientY;
        state.pan.lastClientX = e.clientX;
        state.pan.lastClientY = e.clientY;
        const totalDx = e.clientX - state.pan.startClientX;
        const totalDy = e.clientY - state.pan.startClientY;
        if (Math.abs(totalDx) > CLICK_DEAD_ZONE_PX || Math.abs(totalDy) > CLICK_DEAD_ZONE_PX) {
            state.pan.moved = true;
        }
        state.gPanPx.x += dx;
        state.gPanPx.y += dy;
        applyTransform(state);
    }, { signal });

    function endPan() {
        if (!state.pan) return;
        const wasClickWithoutDrag = (state.pan.button === 0 && !state.pan.moved);
        state.pan = null;
        commitGesture(state, wasClickWithoutDrag);
    }

    gestureSurface.addEventListener('mouseup', endPan, { signal });
    gestureSurface.addEventListener('mouseleave', endPan, { signal });
}

export function setMode(svgId, mode) {
    const state = instances.get(svgId);
    if (!state) return;
    state.mode = mode || 'none';
}

// Toolbar +/- zoom path. The anchor is the visible non-rail centre, computed
// from the gesture-surface rect plus the rail widths so an asymmetric rail
// (host's wide left rail) doesn't pull the zoom focus off the camera centre.
export function zoomByFactorAtCenter(svgId, factor) {
    const state = instances.get(svgId);
    if (!state) return;
    const stageRect = state.gestureSurface.getBoundingClientRect();
    const root = state.svg?.closest('.dnd-mapper-playing');
    const leftEl = root?.querySelector('.dndm-rail--left') ?? null;
    const rightEl = root?.querySelector('.dndm-rail--right') ?? null;
    const leftPx = leftEl ? leftEl.getBoundingClientRect().width : 0;
    const rightPx = rightEl ? rightEl.getBoundingClientRect().width : 0;
    const anchorX = (stageRect.width + leftPx - rightPx) / 2;
    const anchorY = stageRect.height / 2;
    if (applyZoomAtAnchor(state, anchorX, anchorY, factor)) {
        // No gesture state to wait on — flush to C# immediately.
        commitGesture(state, false);
    }
}

export function setViewBox(svgId, panX, panY, zoom) {
    const state = instances.get(svgId);
    if (!state) return;
    if (state.wheelDebounceHandle) { clearTimeout(state.wheelDebounceHandle); state.wheelDebounceHandle = 0; }
    state.pan = null;
    state.gPanPx = { x: 0, y: 0 };
    state.gZoomFactor = 1.0;
    state.panX = panX;
    state.panY = panY;
    state.zoom = clamp(zoom, MIN_ZOOM, MAX_ZOOM);
    applyTransform(state);
}

export function setBounds(svgId, widthCells, heightCells, cellPx) {
    const state = instances.get(svgId);
    if (!state) return;
    state.widthCells = widthCells;
    state.heightCells = heightCells;
    if (cellPx > 0) state.basePxPerCell = cellPx;
    applyTransform(state);
}

export function forceBeginPan(svgId, clientX, clientY, button) {
    const state = instances.get(svgId);
    if (!state || state.pan) return;
    ensureBasePxPerCell(state);
    state.pan = {
        button,
        startClientX: clientX,
        startClientY: clientY,
        lastClientX: clientX,
        lastClientY: clientY,
        moved: false,
    };
}

export function reassertViewBox(_svgId) { /* no-op */ }

export function dispose(svgId) {
    const state = instances.get(svgId);
    if (!state) return;
    try { state.abortController?.abort(); } catch { /* ignore */ }
    if (state.wheelDebounceHandle) {
        try { clearTimeout(state.wheelDebounceHandle); } catch { /* ignore */ }
        state.wheelDebounceHandle = 0;
    }
    if (state.wrapper) {
        state.wrapper.style.transform = '';
    }
    state.dotNetRef = null;
    state.svg = null;
    state.wrapper = null;
    instances.delete(svgId);
}
