const instances = new Map();

/**
 * Sets or clears the visual disabled state on the undo button.
 * A CSS class is used instead of the HTML `disabled` attribute so that clicks still
 * bubble to the container's delegated event listener.
 * @param {Element|null} container
 * @param {boolean} disabled
 */
function setUndoDisabled(container, disabled) {
    container?.querySelector('.toolbar-btn-undo')
        ?.classList.toggle('toolbar-btn-disabled', disabled);
}

/**
 * Updates which swatch button bears the active highlight.
 * @param {Element|null} container
 * @param {string} color - hex or CSS color string
 */
function updateSwatchActive(container, color) {
    container?.querySelectorAll('.toolbar-swatch[data-color]').forEach(s => {
        s.classList.toggle('toolbar-swatch-active',
            s.dataset.color.toLowerCase() === color.toLowerCase());
    });
}

/**
 * Builds a smooth quadratic Bézier path string from an array of {x, y} points.
 * @param {{x: number, y: number}[]} points
 * @returns {string}
 */
function buildPath(points) {
    if (points.length === 1) return `M ${points[0].x} ${points[0].y}`;
    let d = `M ${points[0].x} ${points[0].y}`;
    for (let i = 1; i < points.length - 1; i++) {
        const mx = (points[i].x + points[i + 1].x) / 2;
        const my = (points[i].y + points[i + 1].y) / 2;
        d += ` Q ${points[i].x} ${points[i].y} ${mx} ${my}`;
    }
    const last = points[points.length - 1];
    d += ` L ${last.x} ${last.y}`;
    return d;
}

/**
 * Clones the SVG, injects a background rect, and triggers a browser file download.
 * @param {object} state - Canvas instance state.
 * @param {string} fileName - Download filename.
 * @param {string} [backgroundColor] - Overrides state.backgroundColor when provided.
 */
function triggerSvgDownload(state, fileName, backgroundColor) {
    const svgEl = state.svg;
    const rect = svgEl.getBoundingClientRect();
    const width = Math.round(rect.width);
    const height = Math.round(rect.height);

    const clone = svgEl.cloneNode(true);
    clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
    clone.setAttribute('width', width);
    clone.setAttribute('height', height);
    clone.setAttribute('viewBox', `0 0 ${width} ${height}`);

    // Insert background rect as the first child so it renders behind all strokes.
    const bg = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
    bg.setAttribute('width', '100%');
    bg.setAttribute('height', '100%');
    bg.setAttribute('fill', backgroundColor || state.backgroundColor);
    clone.insertBefore(bg, clone.firstChild);

    const blob = new Blob([new XMLSerializer().serializeToString(clone)],
        { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    setTimeout(() => {
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }, 100);
}

/**
 * Initializes the SVG drawing canvas for a given element ID.
 * All toolbar interactions (swatches, undo, export) are handled via JS event delegation
 * rather than Blazor @onclick bindings, which are not reliably dispatched for RCL
 * components when render mode is applied at the Routes level.
 * @param {string} svgId
 * @param {object} dotNetRef - DotNetObjectReference for JS-to-.NET callbacks.
 * @param {string} initialColor - Initial stroke color.
 * @param {number} initialStrokeWidth - Initial stroke width in pixels.
 * @param {string} initialBackgroundColor - Background color used when exporting.
 */
export function initialize(svgId, dotNetRef, initialColor, initialStrokeWidth, initialBackgroundColor) {
    const svg = document.getElementById(svgId);
    if (!svg) {
        console.error(`[SVGCanvas] initialize: element "${svgId}" not found in the DOM.`);
        return;
    }

    const state = {
        svg,
        dotNetRef,
        color: initialColor,
        strokeWidth: initialStrokeWidth,
        backgroundColor: initialBackgroundColor || 'white',
        isDrawing: false,
        currentPath: null,
        currentPoints: [],
        paths: [],
    };

    instances.set(svgId, state);

    const container = svg.closest('.svg-drawing-canvas');
    const svgPoint = svg.createSVGPoint();

    function getSvgCoords(clientX, clientY) {
        svgPoint.x = clientX;
        svgPoint.y = clientY;
        const ctm = svg.getScreenCTM();
        if (!ctm) return { x: clientX, y: clientY };
        const transformed = svgPoint.matrixTransform(ctm.inverse());
        return { x: transformed.x, y: transformed.y };
    }

    function startStroke(clientX, clientY) {
        state.isDrawing = true;
        const { x, y } = getSvgCoords(clientX, clientY);
        state.currentPoints = [{ x, y }];
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('stroke', state.color);
        path.setAttribute('stroke-width', state.strokeWidth);
        path.setAttribute('fill', 'none');
        path.setAttribute('stroke-linecap', 'round');
        path.setAttribute('stroke-linejoin', 'round');
        path.setAttribute('d', `M ${x} ${y}`);
        svg.appendChild(path);
        state.currentPath = path;
    }

    function continueStroke(clientX, clientY) {
        if (!state.isDrawing || !state.currentPath) return;
        const { x, y } = getSvgCoords(clientX, clientY);
        state.currentPoints.push({ x, y });
        state.currentPath.setAttribute('d', buildPath(state.currentPoints));
    }

    function endStroke() {
        if (!state.isDrawing) return;
        state.isDrawing = false;
        if (!state.currentPath) return;

        if (state.currentPoints.length === 1) {
            // Single tap/click — render as a small filled circle.
            const { x, y } = state.currentPoints[0];
            const dot = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
            dot.setAttribute('cx', x);
            dot.setAttribute('cy', y);
            dot.setAttribute('r', state.strokeWidth / 2);
            dot.setAttribute('fill', state.color);
            svg.replaceChild(dot, state.currentPath);
            state.paths.push(dot);
        } else {
            state.paths.push(state.currentPath);
        }

        state.currentPath = null;
        state.currentPoints = [];
        setUndoDisabled(container, false);
        state.dotNetRef.invokeMethodAsync('OnStrokeCompleted', state.paths.length)
            .catch(err => console.error('[SVGCanvas] OnStrokeCompleted failed.', err));
    }

    // Undo starts disabled; first completed stroke enables it.
    setUndoDisabled(container, true);

    // Color picker
    const colorInput = container?.querySelector('.toolbar-color');
    if (colorInput) {
        colorInput.addEventListener('input', (e) => {
            state.color = e.target.value;
            updateSwatchActive(container, e.target.value);
            state.dotNetRef.invokeMethodAsync('OnColorChanged', e.target.value)
                .catch(err => console.error('[SVGCanvas] OnColorChanged failed.', err));
        });
    } else {
        console.warn('[SVGCanvas] initialize: .toolbar-color not found — color picker will not work.');
    }

    // Size slider
    const sizeInput = container?.querySelector('.toolbar-size');
    const sizeLabel = sizeInput?.closest('.toolbar-group')?.querySelector('.toolbar-label');
    if (sizeInput) {
        sizeInput.addEventListener('input', (e) => {
            const width = parseFloat(e.target.value);
            if (isNaN(width)) return;
            state.strokeWidth = width;
            if (sizeLabel) sizeLabel.textContent = `Size: ${width}`;
            state.dotNetRef.invokeMethodAsync('OnStrokeWidthChanged', width)
                .catch(err => console.error('[SVGCanvas] OnStrokeWidthChanged failed.', err));
        });
    } else {
        console.warn('[SVGCanvas] initialize: .toolbar-size not found — brush size slider will not work.');
    }

    // Delegated click handler for swatches, undo, and export.
    if (container) {
        container.addEventListener('click', (e) => {
            // Swatch
            const swatchEl = e.target.closest('.toolbar-swatch[data-color]');
            if (swatchEl) {
                const color = swatchEl.dataset.color;
                state.color = color;
                if (colorInput) colorInput.value = color;
                updateSwatchActive(container, color);
                state.dotNetRef.invokeMethodAsync('OnColorChanged', color)
                    .catch(err => console.error('[SVGCanvas] OnColorChanged failed.', err));
                return;
            }

            // Undo
            const undoBtn = e.target.closest('.toolbar-btn-undo');
            if (undoBtn) {
                if (undoBtn.classList.contains('toolbar-btn-disabled') || state.paths.length === 0) return;
                state.paths.pop().remove();
                setUndoDisabled(container, state.paths.length === 0);
                state.dotNetRef.invokeMethodAsync('OnStrokeCompleted', state.paths.length)
                    .catch(err => console.error('[SVGCanvas] OnStrokeCompleted failed.', err));
                return;
            }

            // Export
            if (e.target.closest('.toolbar-btn-export')) {
                const now = new Date();
                const pad = n => String(n).padStart(2, '0');
                const ts = `${now.getUTCFullYear()}${pad(now.getUTCMonth() + 1)}${pad(now.getUTCDate())}-${pad(now.getUTCHours())}${pad(now.getUTCMinutes())}${pad(now.getUTCSeconds())}`;
                triggerSvgDownload(state, `drawing-${ts}.svg`);
            }
        });
    }

    // Mouse events
    svg.addEventListener('mousedown', (e) => {
        if (e.button !== 0) return;
        e.preventDefault();
        startStroke(e.clientX, e.clientY);
    });
    svg.addEventListener('mousemove', (e) => continueStroke(e.clientX, e.clientY));
    svg.addEventListener('mouseup', () => endStroke());
    svg.addEventListener('mouseleave', () => endStroke());

    // Touch events
    svg.addEventListener('touchstart', (e) => {
        e.preventDefault();
        startStroke(e.touches[0].clientX, e.touches[0].clientY);
    }, { passive: false });
    svg.addEventListener('touchmove', (e) => {
        e.preventDefault();
        continueStroke(e.touches[0].clientX, e.touches[0].clientY);
    }, { passive: false });
    svg.addEventListener('touchend', (e) => {
        e.preventDefault();
        endStroke();
    }, { passive: false });
}

/**
 * Updates the current stroke color and syncs the color picker and active swatch.
 * @param {string} svgId
 * @param {string} color
 */
export function setColor(svgId, color) {
    const state = instances.get(svgId);
    if (!state) return;
    state.color = color;
    const container = state.svg.closest('.svg-drawing-canvas');
    const colorInput = container?.querySelector('.toolbar-color');
    if (colorInput) colorInput.value = color;
    updateSwatchActive(container, color);
}

/**
 * Updates the current stroke width.
 * @param {string} svgId
 * @param {number} width
 */
export function setStrokeWidth(svgId, width) {
    const state = instances.get(svgId);
    if (!state) return;
    state.strokeWidth = width;
}

/**
 * Removes the most recently drawn stroke. Returns the remaining stroke count.
 * @param {string} svgId
 * @returns {number}
 */
export function undo(svgId) {
    const state = instances.get(svgId);
    if (!state || state.paths.length === 0) return 0;
    state.paths.pop().remove();
    setUndoDisabled(state.svg.closest('.svg-drawing-canvas'), state.paths.length === 0);
    return state.paths.length;
}

/**
 * Removes all strokes from the canvas.
 * @param {string} svgId
 */
export function clear(svgId) {
    const state = instances.get(svgId);
    if (!state) return;
    for (const path of state.paths) path.remove();
    state.paths = [];
    setUndoDisabled(state.svg.closest('.svg-drawing-canvas'), true);
}

/**
 * Triggers a browser file download of the current drawing as an SVG.
 * @param {string} svgId
 * @param {string} fileName
 * @param {string} backgroundColor - Background fill for the exported file.
 */
export function downloadSvg(svgId, fileName, backgroundColor) {
    const state = instances.get(svgId);
    if (!state) return;
    triggerSvgDownload(state, fileName, backgroundColor);
}

/**
 * Cleans up instance state for the given canvas.
 * @param {string} svgId
 */
export function dispose(svgId) {
    instances.delete(svgId);
}
