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

const instances = new Map();

function getSvgCoords(svg, svgPoint, clientX, clientY) {
    svgPoint.x = clientX;
    svgPoint.y = clientY;
    const ctm = svg.getScreenCTM();
    if (!ctm) return { x: clientX, y: clientY };
    const transformed = svgPoint.matrixTransform(ctm.inverse());
    return { x: transformed.x, y: transformed.y };
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
        dotNetRef,
        abortController,
        tokens: new Map(), // tokenId → { x, y, movable }
        widthCells,
        heightCells,
        svgPoint: svg.createSVGPoint(),
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

        const pt = getSvgCoords(svg, state.svgPoint, clientX, clientY);
        state.dragging = {
            tokenId,
            group,
            offsetX: pt.x - info.x,
            offsetY: pt.y - info.y,
            startX: info.x,
            startY: info.y,
            moved: false,
        };
        // Suppress the CSS transition for the duration of the drag so the
        // pointer doesn't lag 150 ms behind the cursor.
        group.style.transition = 'none';
        return true;
    }

    function moveDrag(clientX, clientY) {
        if (!state.dragging) return;
        const pt = getSvgCoords(svg, state.svgPoint, clientX, clientY);
        let nx = pt.x - state.dragging.offsetX;
        let ny = pt.y - state.dragging.offsetY;
        nx = Math.max(0, Math.min(nx, state.widthCells));
        ny = Math.max(0, Math.min(ny, state.heightCells));
        applyTransform(state.dragging.group, nx, ny);
        state.dragging.lastX = nx;
        state.dragging.lastY = ny;
        state.dragging.moved = true;
    }

    function endDrag() {
        if (!state.dragging) return;
        const { tokenId, group, lastX, lastY, startX, startY, moved } = state.dragging;
        // Restore the transition so subsequent remote moves animate.
        group.style.transition = '';
        state.dragging = null;
        if (!moved) return;

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
