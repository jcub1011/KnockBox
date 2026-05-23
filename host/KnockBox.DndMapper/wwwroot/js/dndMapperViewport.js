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
 *
 * Pan is intentionally unbounded — hosts frequently scroll past the map
 * edges to access off-map sticky notes and secondary scenes. Do not
 * reintroduce a ClampPan/setBounds-based limit without consulting that
 * workflow.
 */

import { getStageAnchor } from "./dndMapperSvgMetrics.js";

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

// Rail-aware visible center in world cell coordinates. Uses the centralized
// getStageAnchor so spawn anchor, double-click focus, and button zoom all
// snap to the same reference point — the midpoint of the portion of the
// stage that isn't covered by the left/right rails.
function computeVisibleCenter(state) {
    const base = state.basePxPerCell;
    const anchor = getStageAnchor(state.svgId);
    if (base <= 0 || !anchor) {
        // Fallback: middle of the map in world cells. Reachable only when
        // the playing-phase DOM hasn't materialised yet (which can't happen
        // from commitGesture in normal flow). Crucially this is a true
        // *centre*, not the top-left, so any spawn anchor that reads this
        // lands somewhere sensible instead of at (panX, panY).
        return {
            centerX: state.panX + state.widthCells / 2,
            centerY: state.panY + state.heightCells / 2,
        };
    }
    const centerX = state.panX + anchor.anchorX / (base * state.zoom);
    const centerY = state.panY + anchor.anchorY / (base * state.zoom);
    return { centerX, centerY };
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
    // Image-handle markup inside the wrapper inverse-scales by this var so
    // handles remain a constant on-screen size regardless of zoom. Stay
    // synced with the wrapper transform — same frame, same callsite.
    state.wrapper.style.setProperty('--dndm-inv-zoom', zoom > 0 ? String(1 / zoom) : '1');
    // Also report zoom to ruler-overlay groups so they can update their
    // literal scale attribute. The SVG `transform` attribute does not
    // honor CSS var() — we'd write var(--dndm-inv-zoom) into it and the
    // browser would silently ignore the variable. So instead the viewport
    // walks every [data-dndm-screenpx] node and rewrites its `transform`
    // with a literal `scale(1 / (base * zoom))` value, plus the world-
    // anchor stored on data-anchor-x / data-anchor-y. This keeps the dots
    // and tooltip a constant on-screen size during live wheel-zoom too
    // (Blazor's StateHasChanged only fires after the gesture commits).
    if (base > 0 && zoom > 0) {
        const cellsPerPx = 1 / (base * zoom);
        const scaledNodes = state.wrapper.querySelectorAll('[data-dndm-screenpx]');
        for (const node of scaledNodes) {
            const ax = node.getAttribute('data-anchor-x');
            const ay = node.getAttribute('data-anchor-y');
            if (ax !== null && ay !== null) {
                node.setAttribute('transform', `translate(${ax} ${ay}) scale(${cellsPerPx})`);
            }
        }
    }
    // Mirror pan/zoom into the bitmap canvas renderer so its viewport-sized
    // <canvas> redraws each image at the new screen rect. The canvas module
    // schedules an RAF redraw internally; this call is cheap (just stores
    // the values and marks dirty). When .NET passes the IJSObjectReference
    // for the canvas module into this initialize, JS receives the module's
    // exports namespace — so we call setViewport on it directly.
    if (state.bitmapCanvasModule && state.canvasId) {
        try {
            state.bitmapCanvasModule.setViewport(state.canvasId, panX, panY, zoom, base);
        } catch (err) {
            console.warn('[DndMapperViewport] bitmap canvas setViewport failed; disabling.', err);
            state.bitmapCanvasModule = null;
        }
    }
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
        const { centerX, centerY } = computeVisibleCenter(state);
        state.dotNetRef.invokeMethodAsync(
            'OnViewportChanged', state.panX, state.panY, state.zoom, centerX, centerY, wasClickWithoutDrag)
            .catch(err => console.error('[DndMapperViewport] OnViewportChanged failed.', err));
    }
}

export function initialize(svgId, dotNetRef, panX, panY, zoom, widthCells, heightCells, cellPx, initialMode, bitmapCanvasModule, canvasId) {
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
        svgId,
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
        bitmapCanvasModule: bitmapCanvasModule ?? null,
        canvasId: canvasId ?? null,
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

    // Publish the initial viewport (including the rail-aware visible center)
    // so .NET has a valid center cached before the user interacts. Without
    // this, the first "Add NPC" spawn before any pan/zoom falls back to the
    // map's default spawn position instead of where the host is looking.
    commitGesture(state, false);
}

export function setMode(svgId, mode) {
    const state = instances.get(svgId);
    if (!state) return;
    state.mode = mode || 'none';
}

// Toolbar +/- zoom path. The anchor is the visible non-rail centre, resolved
// via the shared getStageAnchor so an asymmetric rail (host's wide left rail)
// doesn't pull the zoom focus off the camera centre.
export function zoomByFactorAtCenter(svgId, factor) {
    const state = instances.get(svgId);
    if (!state) return;
    const anchor = getStageAnchor(state.svgId);
    if (!anchor) return;
    if (applyZoomAtAnchor(state, anchor.anchorX, anchor.anchorY, factor)) {
        // No gesture state to wait on — flush to C# immediately.
        commitGesture(state, false);
    }
}

export function setViewBox(svgId, panX, panY, zoom) {
    const state = instances.get(svgId);
    if (!state) return;
    state.panX = panX;
    state.panY = panY;
    state.zoom = clamp(zoom, MIN_ZOOM, MAX_ZOOM);
    // commitGesture clears the in-flight gesture state, calls applyTransform,
    // and fires OnViewportChanged back to .NET (including the rail-aware
    // visible center) so the published viewport in .NET stays in sync.
    commitGesture(state, false);
}

// Pan so that the given world cell coord lands at the rail-aware visible
// center of the stage. Same anchor math as zoomByFactorAtCenter and the
// commit-time center used in OnViewportChanged. Commits via commitGesture
// so .NET sees the new viewport (and the new center).
export function centerOnWorld(svgId, worldX, worldY) {
    const state = instances.get(svgId);
    if (!state) return;
    const base = ensureBasePxPerCell(state);
    if (base <= 0) return;
    const anchor = getStageAnchor(state.svgId);
    if (!anchor) return;
    // Discard any in-flight gesture so the anchor math operates against a
    // clean baseline — commitGesture would otherwise fold the partial
    // gesture into the canonical state before applying.
    if (state.wheelDebounceHandle) { clearTimeout(state.wheelDebounceHandle); state.wheelDebounceHandle = 0; }
    state.pan = null;
    state.gPanPx = { x: 0, y: 0 };
    state.gZoomFactor = 1.0;

    state.panX = worldX - anchor.anchorX / (base * state.zoom);
    state.panY = worldY - anchor.anchorY / (base * state.zoom);
    commitGesture(state, false);
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
    state.bitmapCanvasModule = null;
    state.canvasId = null;
    instances.delete(svgId);
}
