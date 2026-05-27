/**
 * DnD Mapper Image Drag — JS-owned drag/resize/rotate for map images.
 *
 * Mirrors dndMapperTokenDrag: per-frame visual updates happen in DOM
 * (no Blazor round-trip), and .NET is only invoked at drag end so the
 * engine can run snap + UpdateImageTransformAsync inside the state
 * Execute lock.
 *
 * Visual elements updated per frame:
 *   - Picker rect (SVG, inside .dndm-canvas-transform) — owns hit-testing
 *     for clicks and stays the selection bound.
 *   - Resize/rotate handles (SVG, inside .dndm-canvas-transform).
 *   - Bitmap canvas in-flight transform — notified via
 *     dndMapperBitmapCanvas.setInFlightTransform so the user sees the
 *     image follow the gesture without a Blazor render round-trip.
 *
 * The cellPx multiplier converts SVG-cell-unit math into stage CSS px.
 */

import { clientToSvgPoint } from "./dndMapperSvgMetrics.js";

const instances = new Map();

const MIN_DIM = 0.1;
// Below this cell-squared movement the drag is treated as a click and
// commits at the original position (matches the C# code path that
// re-snapped to the original transform when the cursor barely moved).
const DRAG_THRESHOLD_SQ = 0.0009;

function getSvgCoords(svgId, clientX, clientY) {
    const pt = clientToSvgPoint(svgId, clientX, clientY);
    return pt ?? { x: clientX, y: clientY };
}

function findImageId(target) {
    if (!(target instanceof Element)) return null;
    const el = target.closest('[data-image-id]');
    return el?.getAttribute('data-image-id') ?? null;
}

function findHandleKind(target) {
    if (!(target instanceof Element)) return null;
    const el = target.closest('[data-image-handle]');
    return el?.getAttribute('data-image-handle') ?? null;
}

function findVisuals(state, imageId) {
    if (!state || !imageId) return null;
    const esc = cssEscape(imageId);
    return {
        picker: state.svg.querySelector(`[data-image-picker-id="${esc}"]`),
        handles: collectHandles(state.svg, esc),
    };
}

function collectHandles(svg, escId) {
    const rotateGroup = svg.querySelector(`[data-image-handles-rotate="${escId}"]`);
    if (!rotateGroup) return null;
    return {
        rotateGroup,
        nw: rotateGroup.querySelector('[data-handle-role="nw"]'),
        ne: rotateGroup.querySelector('[data-handle-role="ne"]'),
        sw: rotateGroup.querySelector('[data-handle-role="sw"]'),
        se: rotateGroup.querySelector('[data-handle-role="se"]'),
        rotline: rotateGroup.querySelector('[data-handle-role="rotline"]'),
        rotcircle: rotateGroup.querySelector('[data-handle-role="rotcircle"]'),
    };
}

function setRectXYWH(el, x, y, w, h) {
    if (!el) return;
    el.setAttribute('x', x);
    el.setAttribute('y', y);
    el.setAttribute('width', w);
    el.setAttribute('height', h);
}

function setRotateTransform(el, rot, cx, cy) {
    if (!el) return;
    el.setAttribute('transform', `rotate(${rot} ${cx} ${cy})`);
}

function paintVisuals(state, visuals, x, y, w, h, rot) {
    if (!visuals) return;
    const cx = x + w / 2;
    const cy = y + h / 2;
    if (visuals.picker) {
        setRectXYWH(visuals.picker, x, y, w, h);
        setRotateTransform(visuals.picker, rot, cx, cy);
    }
    paintHandles(visuals.handles, x, y, w, h, rot);
}

// Each handle is a <g> in MapCanvas.razor with transform="translate(ax ay)
// scale(inv)" where inv = 1/zoom. To move a handle during drag, rewrite that
// transform keeping the inverse-scale so the handle stays constant on-screen
// at every zoom. The SVG `transform` attribute does not parse var() — unlike
// the CSS `transform` property — so we read the wrapper's --dndm-inv-zoom CSS
// variable (kept current by dndMapperViewport.applyTransform) and substitute
// the literal value.
function readInvZoom(el) {
    const wrapper = el && el.closest && el.closest('.dndm-canvas-transform');
    if (!wrapper) return 1;
    const raw = (wrapper.style.getPropertyValue('--dndm-inv-zoom')
        || getComputedStyle(wrapper).getPropertyValue('--dndm-inv-zoom') || '').trim();
    if (!raw) return 1;
    const n = parseFloat(raw);
    return Number.isFinite(n) && n > 0 ? n : 1;
}

function setAnchoredHandle(el, ax, ay) {
    if (!el) return;
    const inv = readInvZoom(el);
    el.setAttribute('transform', `translate(${ax} ${ay}) scale(${inv})`);
}

function paintHandles(handles, x, y, w, h, rot) {
    if (!handles) return;
    const cx = x + w / 2;
    const cy = y + h / 2;
    setRotateTransform(handles.rotateGroup, rot, cx, cy);
    setAnchoredHandle(handles.nw, x, y);
    setAnchoredHandle(handles.ne, x + w, y);
    setAnchoredHandle(handles.sw, x, y + h);
    setAnchoredHandle(handles.se, x + w, y + h);
    if (handles.rotline) {
        handles.rotline.setAttribute('x1', cx);
        handles.rotline.setAttribute('y1', y);
        handles.rotline.setAttribute('x2', cx);
        handles.rotline.setAttribute('y2', y - 0.6);
    }
    setAnchoredHandle(handles.rotcircle, cx, y - 0.6);
}

// Notify the bitmap canvas renderer of the in-flight transform so the
// rendered image follows the gesture every frame. Cleared on drag end.
// When .NET passes the IJSObjectReference for dndMapperBitmapCanvas into
// initialize, the JS side receives the imported module's exports
// namespace — calling setInFlightTransform on it works directly.
function pushCanvasInFlight(state, imageId, x, y, w, h, rot) {
    if (!state.bitmapCanvasModule || !state.canvasId) return;
    try {
        state.bitmapCanvasModule.setInFlightTransform(state.canvasId, imageId, {
            x, y, width: w, height: h, rotation: rot,
        });
    } catch (err) {
        console.warn('[DndMapperImageDrag] bitmap canvas setInFlightTransform failed; disabling.', err);
        state.bitmapCanvasModule = null;
    }
}

function clearCanvasInFlight(state, imageId) {
    if (!state.bitmapCanvasModule || !state.canvasId) return;
    try { state.bitmapCanvasModule.clearInFlightTransform(state.canvasId, imageId); }
    catch (err) {
        console.warn('[DndMapperImageDrag] bitmap canvas clearInFlightTransform failed; disabling.', err);
        state.bitmapCanvasModule = null;
    }
}

// ── Drag math (mirrors MapCanvas.razor.cs ApplyDragDelta + ApplyResize) ──

function applyBodyDelta(orig, dx, dy) {
    return {
        x: orig.x + dx,
        y: orig.y + dy,
        w: orig.w,
        h: orig.h,
        rot: orig.rot,
    };
}

function applyResize(orig, kind, dx, dy, freeAspect) {
    const east = (kind === 'ne' || kind === 'se');
    const south = (kind === 'sw' || kind === 'se');

    let rawW = east ? orig.w + dx : orig.w - dx;
    let rawH = south ? orig.h + dy : orig.h - dy;
    rawW = Math.max(MIN_DIM, rawW);
    rawH = Math.max(MIN_DIM, rawH);

    let newW = rawW;
    let newH = rawH;
    if (!freeAspect && orig.w > 0 && orig.h > 0) {
        const scaleW = rawW / orig.w;
        const scaleH = rawH / orig.h;
        const scale = Math.abs(scaleW - 1.0) >= Math.abs(scaleH - 1.0) ? scaleW : scaleH;
        newW = Math.max(MIN_DIM, orig.w * scale);
        newH = Math.max(MIN_DIM, orig.h * scale);
    }

    const newX = east ? orig.x : orig.x + (orig.w - newW);
    const newY = south ? orig.y : orig.y + (orig.h - newH);
    return { x: newX, y: newY, w: newW, h: newH, rot: orig.rot };
}

function applyRotate(orig, dx, dy) {
    // Rotate handle starts above the image center; treat start direction as
    // -Y and derive a relative angle from the cursor delta (cell-space).
    const startAngle = Math.atan2(-1, 0);
    const currentAngle = Math.atan2(-1 + dy, dx);
    const deg = (currentAngle - startAngle) * 180.0 / Math.PI;
    let rot = (orig.rot + deg) % 360.0;
    return { x: orig.x, y: orig.y, w: orig.w, h: orig.h, rot };
}

function computeDrag(orig, kind, dx, dy, freeAspect) {
    if (kind === 'body') return applyBodyDelta(orig, dx, dy);
    if (kind === 'rot') return applyRotate(orig, dx, dy);
    return applyResize(orig, kind, dx, dy, freeAspect);
}

/**
 * @param {string} svgId
 * @param {object} dotNetRef
 * @param {Array<{imageId: string, locked: boolean}>} images
 * @param {number} cellPx Pixels per map cell — used to convert cell-unit
 *                       drag math into CSS px for the HTML <img> elements.
 */
export function initialize(svgId, dotNetRef, images, cellPx, bitmapCanvasModule, canvasId) {
    const svg = document.getElementById(svgId);
    if (!svg) {
        console.error(`[DndMapperImageDrag] initialize: element "${svgId}" not found.`);
        return;
    }

    dispose(svgId);

    const wrapper = svg.closest('.dndm-canvas-transform');
    if (!wrapper) {
        console.error(`[DndMapperImageDrag] initialize: .dndm-canvas-transform ancestor for "${svgId}" not found.`);
        return;
    }

    const abortController = new AbortController();
    const signal = abortController.signal;

    const state = {
        svg,
        svgId,
        wrapper,
        cellPx: cellPx > 0 ? cellPx : 1,
        dotNetRef,
        abortController,
        images: new Map(),
        dragging: null,
        bitmapCanvasModule: bitmapCanvasModule ?? null,
        canvasId: canvasId ?? null,
    };
    instances.set(svgId, state);

    for (const i of images || []) {
        state.images.set(i.imageId, { locked: !!i.locked });
    }

    // Window-level move/up handlers are attached only while a drag is in
    // flight. Binding to the SVG and relying on mouseleave to end the drag
    // would cancel resizes the moment a fast cursor escaped the SVG bounds.
    let windowMouseMove = null;
    let windowMouseUp = null;
    let windowTouchMove = null;
    let windowTouchEnd = null;

    function attachWindowMouseListeners() {
        if (windowMouseMove) return;
        windowMouseMove = (e) => {
            if (state.dragging) moveDrag(e.clientX, e.clientY, e.shiftKey, e.ctrlKey);
        };
        windowMouseUp = () => endDrag();
        window.addEventListener('mousemove', windowMouseMove);
        window.addEventListener('mouseup', windowMouseUp);
    }

    function attachWindowTouchListeners() {
        if (windowTouchMove) return;
        windowTouchMove = (e) => {
            if (!state.dragging) return;
            e.preventDefault();
            const touch = e.touches[0];
            moveDrag(touch.clientX, touch.clientY, e.shiftKey, e.ctrlKey);
        };
        windowTouchEnd = () => endDrag();
        window.addEventListener('touchmove', windowTouchMove, { passive: false });
        window.addEventListener('touchend', windowTouchEnd);
        window.addEventListener('touchcancel', windowTouchEnd);
    }

    function detachWindowListeners() {
        if (windowMouseMove) {
            window.removeEventListener('mousemove', windowMouseMove);
            window.removeEventListener('mouseup', windowMouseUp);
            windowMouseMove = null;
            windowMouseUp = null;
        }
        if (windowTouchMove) {
            window.removeEventListener('touchmove', windowTouchMove);
            window.removeEventListener('touchend', windowTouchEnd);
            window.removeEventListener('touchcancel', windowTouchEnd);
            windowTouchMove = null;
            windowTouchEnd = null;
        }
    }

    function startDrag(target, clientX, clientY, shiftKey, ctrlKey) {
        const imageId = findImageId(target);
        if (!imageId) return false;
        const info = state.images.get(imageId);
        if (!info || info.locked) return false;

        const handleKind = findHandleKind(target);
        const kind = handleKind || 'body';

        const visuals = findVisuals(state, imageId);
        if (!visuals || !visuals.picker) return false;

        // Read canonical drag-start values from the picker rect (SVG cell
        // units). The HTML <img> sibling uses CSS px so reading its style
        // would require an extra unit conversion; the picker already
        // mirrors the canonical position and is the source of truth here.
        const orig = {
            x: parseFloat(visuals.picker.getAttribute('x') || '0'),
            y: parseFloat(visuals.picker.getAttribute('y') || '0'),
            w: parseFloat(visuals.picker.getAttribute('width') || '0'),
            h: parseFloat(visuals.picker.getAttribute('height') || '0'),
            rot: parseRotation(visuals.picker.getAttribute('transform')),
        };

        const pt = getSvgCoords(svgId, clientX, clientY);

        state.dragging = {
            imageId,
            kind,
            visuals,
            orig,
            startSvg: pt,
            last: { ...orig },
            moved: false,
            shiftKey: !!shiftKey,
            ctrlKey: !!ctrlKey,
        };

        suppressTransitions(visuals, true);
        return true;
    }

    function moveDrag(clientX, clientY, shiftKey, ctrlKey) {
        const d = state.dragging;
        if (!d) return;
        const pt = getSvgCoords(svgId, clientX, clientY);
        const dx = pt.x - d.startSvg.x;
        const dy = pt.y - d.startSvg.y;
        d.shiftKey = !!shiftKey;
        d.ctrlKey = !!ctrlKey;
        const next = computeDrag(d.orig, d.kind, dx, dy, d.shiftKey);
        d.last = next;
        paintVisuals(state, d.visuals, next.x, next.y, next.w, next.h, next.rot);
        pushCanvasInFlight(state, d.imageId, next.x, next.y, next.w, next.h, next.rot);
        if (dx * dx + dy * dy >= DRAG_THRESHOLD_SQ) d.moved = true;
    }

    function endDrag() {
        const d = state.dragging;
        detachWindowListeners();
        if (!d) return;
        state.dragging = null;
        suppressTransitions(d.visuals, false);

        if (!d.moved) {
            // Sub-threshold: revert any preview to canonical (orig) so a
            // simple click doesn't leave the image shifted.
            paintVisuals(state, d.visuals, d.orig.x, d.orig.y, d.orig.w, d.orig.h, d.orig.rot);
            clearCanvasInFlight(state, d.imageId);
            return;
        }

        // The canonical state will land via OnImageDragEnd → state.Maps
        // mutation → PushImagesToBitmapCanvas re-marshal. Clearing the
        // in-flight override here means a sub-frame stale render is
        // possible right at drop, but the next RAF picks up the new
        // canonical position and is indistinguishable in practice.
        clearCanvasInFlight(state, d.imageId);

        if (state.dotNetRef) {
            const k = d.kind;
            state.dotNetRef.invokeMethodAsync(
                'OnImageDragEnd',
                d.imageId, k,
                d.orig.x, d.orig.y, d.orig.w, d.orig.h, d.orig.rot,
                d.last.x, d.last.y, d.last.w, d.last.h, d.last.rot,
                d.ctrlKey, d.shiftKey)
                .catch(err => console.error('[DndMapperImageDrag] OnImageDragEnd failed.', err));
        }
    }

    // Capture phase: the picker <rect> uses Blazor's @onmousedown:stopPropagation
    // to keep the SVG's @onmousedown (focus-box / fog-paint entry) from firing
    // on image clicks. That stop happens at the picker during bubble phase, so a
    // bubble-phase listener on the SVG would never see the event. Capture phase
    // runs root→target before any bubble-phase handler can stop propagation.
    svg.addEventListener('mousedown', (e) => {
        if (e.button !== 0) return;
        if (startDrag(e.target, e.clientX, e.clientY, e.shiftKey, e.ctrlKey)) {
            e.preventDefault();
            attachWindowMouseListeners();
        }
    }, { capture: true, signal });

    svg.addEventListener('touchstart', (e) => {
        if (e.touches.length !== 1) return;
        const touch = e.touches[0];
        if (startDrag(touch.target, touch.clientX, touch.clientY, e.shiftKey, e.ctrlKey)) {
            e.preventDefault();
            e.stopPropagation();
            attachWindowTouchListeners();
        }
    }, { passive: false, capture: true, signal });

    // Detach hook so endDrag() can clean up the window listeners.
    state.detachWindowListeners = detachWindowListeners;
}

function parseRotation(transform) {
    if (!transform) return 0;
    const m = /rotate\(\s*([-\d.]+)/.exec(transform);
    return m ? parseFloat(m[1]) : 0;
}

function suppressTransitions(visuals, on) {
    if (!visuals) return;
    const value = on ? 'none' : '';
    if (visuals.picker) visuals.picker.style.transition = value;
    if (visuals.handles?.rotateGroup) visuals.handles.rotateGroup.style.transition = value;
}

/**
 * Refresh the list of interactive images. Idempotent.
 * @param {string} svgId
 * @param {Array<{imageId: string, locked: boolean}>} images
 * @param {number} cellPx Optional updated cellPx (positive to apply).
 */
export function setImages(svgId, images, cellPx) {
    const state = instances.get(svgId);
    if (!state) return;
    state.images.clear();
    for (const i of images || []) {
        state.images.set(i.imageId, { locked: !!i.locked });
    }
    if (cellPx > 0) state.cellPx = cellPx;
}

/**
 * Re-applies canonical coords from server state. Used after engine rejection
 * to undo an optimistic preview, or after a snap commit landed on a position
 * Blazor's diff thinks is unchanged.
 * @param {string} svgId
 * @param {string} imageId
 * @param {number} x @param {number} y @param {number} w @param {number} h
 * @param {number} rot
 */
export function reconcileImage(svgId, imageId, x, y, w, h, rot) {
    const state = instances.get(svgId);
    if (!state) return;
    // Don't stomp on the user's in-flight drag preview.
    if (state.dragging?.imageId === imageId) return;
    const visuals = findVisuals(state, imageId);
    suppressTransitions(visuals, false);
    paintVisuals(state, visuals, x, y, w, h, rot);
}

function cssEscape(value) {
    if (typeof CSS !== 'undefined' && typeof CSS.escape === 'function') return CSS.escape(value);
    return String(value).replace(/[^a-zA-Z0-9-_]/g, (c) => `\\${c}`);
}

export function dispose(svgId) {
    const state = instances.get(svgId);
    if (!state) return;
    try { state.detachWindowListeners?.(); } catch { /* ignore */ }
    try { state.abortController?.abort(); } catch { /* ignore */ }
    // If a drag was mid-flight, clear the canvas override so the canvas
    // doesn't keep painting a stale in-flight position after teardown.
    if (state.dragging) {
        clearCanvasInFlight(state, state.dragging.imageId);
    }
    state.dotNetRef = null;
    state.images.clear();
    state.svg = null;
    state.wrapper = null;
    state.dragging = null;
    state.bitmapCanvasModule = null;
    state.canvasId = null;
    instances.delete(svgId);
}
