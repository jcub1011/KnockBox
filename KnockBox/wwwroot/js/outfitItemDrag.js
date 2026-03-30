/**
 * Outfit Item Drag — lightweight drag-positioning module for clothing items.
 *
 * Manages an SVG overlay containing clothing items as <g> groups with transforms.
 * Supports mouse and touch drag to reposition items within the viewBox bounds.
 * Calls back to .NET on drag end via OnItemMoved(typeId, x, y).
 */

const instances = new Map();

/**
 * Converts screen (client) coordinates to SVG viewBox coordinates.
 * Uses the same getScreenCTM().inverse() pattern as svgDrawingCanvas.js.
 * @param {SVGSVGElement} svg
 * @param {SVGPoint} svgPoint - Reusable SVGPoint for transforms.
 * @param {number} clientX
 * @param {number} clientY
 * @returns {{x: number, y: number}}
 */
function getSvgCoords(svg, svgPoint, clientX, clientY) {
    svgPoint.x = clientX;
    svgPoint.y = clientY;
    const ctm = svg.getScreenCTM();
    if (!ctm) return { x: clientX, y: clientY };
    const transformed = svgPoint.matrixTransform(ctm.inverse());
    return { x: transformed.x, y: transformed.y };
}

/**
 * Initializes the drag layer for a given SVG element.
 * @param {string} svgId - DOM id of the drag-layer SVG element.
 * @param {object} dotNetRef - DotNetObjectReference for JS-to-.NET callbacks.
 * @param {Array<{typeId: string, svgContent: string, x: number, y: number, width: number, height: number}>} items
 * @param {number} viewBoxWidth
 * @param {number} viewBoxHeight
 */
export function initialize(svgId, dotNetRef, items, viewBoxWidth, viewBoxHeight) {
    const svg = document.getElementById(svgId);
    if (!svg) {
        console.error(`[OutfitItemDrag] initialize: element "${svgId}" not found.`);
        return;
    }

    const state = {
        svg,
        dotNetRef,
        items: new Map(),          // typeId → { group, x, y, width, height }
        viewBoxWidth,
        viewBoxHeight,
        svgPoint: svg.createSVGPoint(),
        dragging: null,            // { typeId, offsetX, offsetY }
        selectedTypeId: null,
    };

    instances.set(svgId, state);

    // Build item groups
    for (const item of items) {
        const group = document.createElementNS('http://www.w3.org/2000/svg', 'g');
        group.setAttribute('data-type-id', item.typeId);
        group.setAttribute('transform', `translate(${item.x},${item.y})`);
        group.style.cursor = 'grab';

        // Parse and append item SVG content
        const parser = new DOMParser();
        const doc = parser.parseFromString(
            `<svg xmlns="http://www.w3.org/2000/svg">${item.svgContent}</svg>`,
            'image/svg+xml'
        );
        for (const child of [...doc.documentElement.childNodes]) {
            group.appendChild(document.importNode(child, true));
        }

        // Add a transparent hit-area rect so the entire item bounding box is draggable
        const hitRect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
        hitRect.setAttribute('width', item.width);
        hitRect.setAttribute('height', item.height);
        hitRect.setAttribute('fill', 'transparent');
        hitRect.setAttribute('stroke', 'none');
        group.insertBefore(hitRect, group.firstChild);

        svg.appendChild(group);
        state.items.set(item.typeId, {
            group,
            x: item.x,
            y: item.y,
            width: item.width,
            height: item.height,
        });
    }

    // ── Selection highlight ────────────────────────────────────────────────
    const highlight = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
    highlight.setAttribute('fill', 'none');
    highlight.setAttribute('stroke', '#7c3aed');
    highlight.setAttribute('stroke-width', '4');
    highlight.setAttribute('stroke-dasharray', '12 6');
    highlight.setAttribute('rx', '6');
    highlight.setAttribute('ry', '6');
    highlight.setAttribute('pointer-events', 'none');
    highlight.style.display = 'none';
    svg.appendChild(highlight);
    state.highlight = highlight;

    // ── Drag handlers ──────────────────────────────────────────────────────

    function findItemAtPoint(clientX, clientY) {
        const pt = getSvgCoords(svg, state.svgPoint, clientX, clientY);
        // Check items in reverse (top-most first)
        const entries = [...state.items.entries()].reverse();
        for (const [typeId, info] of entries) {
            if (pt.x >= info.x && pt.x <= info.x + info.width &&
                pt.y >= info.y && pt.y <= info.y + info.height) {
                return { typeId, offsetX: pt.x - info.x, offsetY: pt.y - info.y };
            }
        }
        return null;
    }

    function startDrag(clientX, clientY) {
        const hit = findItemAtPoint(clientX, clientY);
        if (!hit) return;
        state.dragging = hit;
        const info = state.items.get(hit.typeId);
        if (info) info.group.style.cursor = 'grabbing';
        setSelectedItem(svgId, hit.typeId);
    }

    function moveDrag(clientX, clientY) {
        if (!state.dragging) return;
        const pt = getSvgCoords(svg, state.svgPoint, clientX, clientY);
        const info = state.items.get(state.dragging.typeId);
        if (!info) return;

        let newX = pt.x - state.dragging.offsetX;
        let newY = pt.y - state.dragging.offsetY;

        // Loosen clamping to allow horizontal shifting (e.g. up to 50% off-screen).
        // This ensures players can still position items even if the canvas is narrow.
        const hMargin = info.width * 0.5;
        newX = Math.max(-hMargin, Math.min(newX, state.viewBoxWidth - info.width + hMargin));

        // Loosen vertical clamping to allow items to be moved partially (50%) outside.
        const vMargin = info.height * 0.5;
        newY = Math.max(-vMargin, Math.min(newY, state.viewBoxHeight - info.height + vMargin));
        info.x = newX;
        info.y = newY;
        info.group.setAttribute('transform', `translate(${newX},${newY})`);
        updateHighlight(state);
    }

    function endDrag() {
        if (!state.dragging) return;
        const typeId = state.dragging.typeId;
        const info = state.items.get(typeId);
        if (info) {
            info.group.style.cursor = 'grab';
            state.dotNetRef.invokeMethodAsync('OnItemMoved', typeId, info.x, info.y)
                .catch(err => console.error('[OutfitItemDrag] OnItemMoved failed.', err));
        }
        state.dragging = null;
    }

    // Mouse events
    svg.addEventListener('mousedown', (e) => {
        if (e.button !== 0) return;
        e.preventDefault();
        startDrag(e.clientX, e.clientY);
    });
    svg.addEventListener('mousemove', (e) => moveDrag(e.clientX, e.clientY));
    svg.addEventListener('mouseup', () => endDrag());
    svg.addEventListener('mouseleave', () => endDrag());

    // Touch events
    svg.addEventListener('touchstart', (e) => {
        e.preventDefault();
        startDrag(e.touches[0].clientX, e.touches[0].clientY);
    }, { passive: false });
    svg.addEventListener('touchmove', (e) => {
        e.preventDefault();
        moveDrag(e.touches[0].clientX, e.touches[0].clientY);
    }, { passive: false });
    svg.addEventListener('touchend', (e) => {
        e.preventDefault();
        endDrag();
    }, { passive: false });
}

/**
 * Updates the dashed highlight rectangle to surround the currently selected item.
 * @param {object} state
 */
function updateHighlight(state) {
    if (!state.selectedTypeId || !state.items.has(state.selectedTypeId)) {
        state.highlight.style.display = 'none';
        return;
    }
    const info = state.items.get(state.selectedTypeId);
    const pad = 4;
    state.highlight.setAttribute('x', info.x - pad);
    state.highlight.setAttribute('y', info.y - pad);
    state.highlight.setAttribute('width', info.width + pad * 2);
    state.highlight.setAttribute('height', info.height + pad * 2);
    state.highlight.style.display = '';
}

/**
 * Sets the visually selected item (dashed outline).
 * @param {string} svgId
 * @param {string} typeId
 */
export function setSelectedItem(svgId, typeId) {
    const state = instances.get(svgId);
    if (!state) return;
    state.selectedTypeId = typeId;
    updateHighlight(state);
}

/**
 * Programmatically moves an item to the given position (e.g. from manual X/Y inputs).
 * @param {string} svgId
 * @param {string} typeId
 * @param {number} x
 * @param {number} y
 */
export function updateItemPosition(svgId, typeId, x, y) {
    const state = instances.get(svgId);
    if (!state) return;
    const info = state.items.get(typeId);
    if (!info) return;

    x = Math.max(-info.width * 0.5, Math.min(x, state.viewBoxWidth - info.width * 0.5));
    y = Math.max(-info.height * 0.5, Math.min(y, state.viewBoxHeight - info.height * 0.5));

    info.x = x;
    info.y = y;
    info.group.setAttribute('transform', `translate(${x},${y})`);
    updateHighlight(state);
}

/**
 * Returns current positions for all items.
 * @param {string} svgId
 * @returns {Object<string, {x: number, y: number}>}
 */
export function getPositions(svgId) {
    const state = instances.get(svgId);
    if (!state) return {};
    const result = {};
    for (const [typeId, info] of state.items) {
        result[typeId] = { x: info.x, y: info.y };
    }
    return result;
}

/**
 * Cleans up instance state.
 * @param {string} svgId
 */
export function dispose(svgId) {
    instances.delete(svgId);
}
