/**
 * DnD Mapper Token Drag — viewBox-aware drag interop for SVG token groups.
 *
 * Attaches mouse + touch listeners to the parent SVG; drag is delegated by
 * reading the [data-token-id] attribute on the event target. Blazor renders the
 * token <g> elements and their canonical transform attribute; this module only
 * mutates the transform during an active drag, then defers back to Blazor (after
 * a successful engine move re-renders the attribute) or to revertToken (after
 * a rejected move).
 */

import { clientToSvgPoint } from "./dndMapperSvgMetrics.js";

const instances = new Map();

function getSvgCoords(svgId, clientX, clientY) {
    const pt = clientToSvgPoint(svgId, clientX, clientY);
    return pt ?? { x: clientX, y: clientY };
}

function findTokenGroup(target) {
    if (!(target instanceof Element)) return null;
    return target.closest('[data-token-id]');
}

function applyTransform(group, x, y) {
    group.setAttribute('transform', `translate(${x} ${y})`);
}

/**
 * @param {string} svgId
 * @param {object} dotNetRef
 * @param {Array<{tokenId: string, x: number, y: number, movable: boolean}>} tokens
 * @param {number} widthCells
 * @param {number} heightCells
 */
export function initialize(svgId, dotNetRef, tokens, widthCells, heightCells) {
    const svg = document.getElementById(svgId);
    if (!svg) {
        console.error(`[DndMapperTokenDrag] initialize: element "${svgId}" not found.`);
        return;
    }

    // If a previous instance is still registered (e.g. hot reload), tear it down.
    dispose(svgId);

    const abortController = new AbortController();
    const signal = abortController.signal;

    const state = {
        svg,
        svgId,
        dotNetRef,
        abortController,
        tokens: new Map(), // tokenId → { x, y, movable }
        widthCells,
        heightCells,
        dragging: null, // { tokenId, group, offsetX, offsetY, startX, startY, moved }
    };
    instances.set(svgId, state);

    for (const t of tokens || []) {
        state.tokens.set(t.tokenId, { x: t.x, y: t.y, movable: !!t.movable });
    }

    function startDrag(target, clientX, clientY) {
        const group = findTokenGroup(target);
        if (!group) return false;
        const tokenId = group.getAttribute('data-token-id');
        if (!tokenId) return false;
        const info = state.tokens.get(tokenId);
        if (!info || !info.movable) return false;

        // Anchor the drag on the GROUP'S actual rendered position rather than the
        // canonical token X/Y. For regular tokens these match. For stack-popover
        // chips they don't — using the chip's real position keeps the cursor
        // glued to the chip throughout the drag.
        const transform = group.getAttribute('transform') || '';
        const m = /translate\(\s*([-\d.]+)\s+([-\d.]+)\s*\)/.exec(transform);
        const startX = m ? parseFloat(m[1]) : info.x;
        const startY = m ? parseFloat(m[2]) : info.y;

        const pt = getSvgCoords(svgId, clientX, clientY);
        state.dragging = {
            tokenId,
            group,
            offsetX: pt.x - startX,
            offsetY: pt.y - startY,
            startX,
            startY,
            moved: false,
        };
        // Suppress the CSS transition for the duration of the drag so the
        // pointer doesn't lag 150 ms behind the cursor.
        group.style.transition = 'none';
        return true;
    }

    // Squared distance (in SVG/cell units) below which a drag is treated as a
    // click — avoids snapping a token into a neighbouring cell when the user
    // taps a chip without intending to move it.
    const DRAG_THRESHOLD_SQ = 0.09; // 0.3 cells

    function moveDrag(clientX, clientY) {
        if (!state.dragging) return;
        const pt = getSvgCoords(svgId, clientX, clientY);
        let nx = pt.x - state.dragging.offsetX;
        let ny = pt.y - state.dragging.offsetY;
        nx = Math.max(0, Math.min(nx, state.widthCells));
        ny = Math.max(0, Math.min(ny, state.heightCells));
        applyTransform(state.dragging.group, nx, ny);
        state.dragging.lastX = nx;
        state.dragging.lastY = ny;
        const dx = nx - state.dragging.startX;
        const dy = ny - state.dragging.startY;
        if (dx * dx + dy * dy >= DRAG_THRESHOLD_SQ) state.dragging.moved = true;
    }

    function endDrag() {
        if (!state.dragging) return;
        const { tokenId, group, lastX, lastY, startX, startY, moved } = state.dragging;
        // Restore the transition so subsequent remote moves animate.
        group.style.transition = '';
        state.dragging = null;
        if (!moved) {
            // Restore the group's transform to its pre-drag position so a
            // sub-threshold drag doesn't leave the visual stranded mid-cell.
            applyTransform(group, startX, startY);
            return;
        }

        const finalX = lastX ?? startX;
        const finalY = lastY ?? startY;
        // Tentatively keep the visual position; .NET will either confirm via a
        // state-change push (setMovableTokens) or revert via revertToken.
        if (state.dotNetRef) {
            state.dotNetRef.invokeMethodAsync('OnTokenDragEnd', tokenId, finalX, finalY)
                .catch(err => console.error('[DndMapperTokenDrag] OnTokenDragEnd failed.', err));
        }
    }

    svg.addEventListener('mousedown', (e) => {
        if (e.button !== 0) return;
        if (startDrag(e.target, e.clientX, e.clientY)) {
            e.preventDefault();
            e.stopPropagation();
        }
    }, { signal });
    svg.addEventListener('mousemove', (e) => {
        if (state.dragging) moveDrag(e.clientX, e.clientY);
    }, { signal });
    svg.addEventListener('mouseup', () => endDrag(), { signal });
    svg.addEventListener('mouseleave', () => endDrag(), { signal });

    svg.addEventListener('touchstart', (e) => {
        if (e.touches.length !== 1) return;
        const touch = e.touches[0];
        if (startDrag(touch.target, touch.clientX, touch.clientY)) {
            e.preventDefault();
            e.stopPropagation();
        }
    }, { passive: false, signal });
    svg.addEventListener('touchmove', (e) => {
        if (!state.dragging) return;
        e.preventDefault();
        const touch = e.touches[0];
        moveDrag(touch.clientX, touch.clientY);
    }, { passive: false, signal });
    svg.addEventListener('touchend', () => endDrag(), { passive: false, signal });
    svg.addEventListener('touchcancel', () => endDrag(), { passive: false, signal });
}

/**
 * @param {string} svgId
 * @param {Array<{tokenId: string, x: number, y: number, movable: boolean}>} tokens
 */
export function setMovableTokens(svgId, tokens) {
    const state = instances.get(svgId);
    if (!state) return;
    state.tokens.clear();
    for (const t of tokens || []) {
        state.tokens.set(t.tokenId, { x: t.x, y: t.y, movable: !!t.movable });
    }
}

/**
 * Updates the clamp bounds applied during drag (needed after a map swap whose
 * new grid has different dimensions). No-op if the instance is gone.
 * @param {string} svgId
 * @param {number} widthCells
 * @param {number} heightCells
 */
export function setBounds(svgId, widthCells, heightCells) {
    const state = instances.get(svgId);
    if (!state) return;
    state.widthCells = widthCells;
    state.heightCells = heightCells;
}

/**
 * Re-applies the canonical position from instance state to the token group.
 * Used after engine rejection to undo the optimistic visual placement.
 * @param {string} svgId
 * @param {string} tokenId
 */
export function revertToken(svgId, tokenId) {
    const state = instances.get(svgId);
    if (!state) return;
    const info = state.tokens.get(tokenId);
    if (!info) return;
    const group = state.svg?.querySelector(`[data-token-id="${cssEscape(tokenId)}"]`);
    if (!group) return;
    group.style.transition = '';
    applyTransform(group, info.x, info.y);
}

/**
 * Force the visual transform back to the authoritative coords after an
 * engine-accepted move. Needed when the server's snap lands on the same
 * cell the drag started in — Blazor's SVG diff sees no transform change
 * and skips the DOM update, leaving the token stranded at the JS-applied
 * drag-end position.
 * @param {string} svgId
 * @param {string} tokenId
 * @param {number} x
 * @param {number} y
 */
export function reconcileToken(svgId, tokenId, x, y) {
    const state = instances.get(svgId);
    if (!state) return;
    if (state.dragging?.tokenId === tokenId) return;
    const info = state.tokens.get(tokenId);
    if (info) { info.x = x; info.y = y; }
    const group = state.svg?.querySelector(`[data-token-id="${cssEscape(tokenId)}"]`);
    if (!group) return;
    group.style.transition = '';
    applyTransform(group, x, y);
}

function cssEscape(value) {
    if (typeof CSS !== 'undefined' && typeof CSS.escape === 'function') return CSS.escape(value);
    return String(value).replace(/[^a-zA-Z0-9-_]/g, (c) => `\\${c}`);
}

export function dispose(svgId) {
    const state = instances.get(svgId);
    if (!state) return;
    try { state.abortController?.abort(); } catch { /* ignore */ }
    state.dotNetRef = null;
    state.tokens.clear();
    state.svg = null;
    instances.delete(svgId);
}
