const instances = new Map();

/**
 * Sets or clears the visual disabled state on the undo button.
 * We use a CSS class instead of the HTML `disabled` attribute so that
 * the click event still bubbles to the container's event-delegation listener.
 * @param {Element|null} container
 * @param {boolean} disabled
 */
function setUndoDisabled(container, disabled) {
    const btn = container?.querySelector('.toolbar-btn-undo');
    if (btn) {
        btn.classList.toggle('toolbar-btn-disabled', disabled);
        console.log(`[SVGCanvas] setUndoDisabled: disabled=${disabled}`);
    }
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
 * Initializes the SVG drawing canvas for a given element ID.
 * @param {string} svgId - The ID of the SVG element.
 * @param {object} dotNetRef - The .NET object reference for JS-to-.NET callbacks.
 * @param {string} initialColor - The initial stroke color.
 * @param {number} initialStrokeWidth - The initial stroke width in pixels.
 * @param {string} initialBackgroundColor - The canvas background color (used when exporting).
 */
export function initialize(svgId, dotNetRef, initialColor, initialStrokeWidth, initialBackgroundColor) {
    console.log(`[SVGCanvas] initialize called — svgId="${svgId}", initialColor="${initialColor}", initialStrokeWidth=${initialStrokeWidth}, backgroundColor="${initialBackgroundColor}"`);

    const svg = document.getElementById(svgId);
    if (!svg) {
        console.error(`[SVGCanvas] initialize: SVG element with id "${svgId}" not found in the DOM.`);
        return;
    }
    console.log(`[SVGCanvas] initialize: SVG element found.`, svg);

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
    console.log(`[SVGCanvas] initialize: State registered. Total instances: ${instances.size}`);

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
        console.log(`[SVGCanvas] startStroke: color="${state.color}", strokeWidth=${state.strokeWidth}, svgCoords={x:${x.toFixed(1)}, y:${y.toFixed(1)}}`);

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
        if (state.currentPath) {
            if (state.currentPoints.length === 1) {
                // Single tap/click — render as a small filled circle
                const { x, y } = state.currentPoints[0];
                const r = state.strokeWidth / 2;
                const dot = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
                dot.setAttribute('cx', x);
                dot.setAttribute('cy', y);
                dot.setAttribute('r', r);
                dot.setAttribute('fill', state.color);
                svg.replaceChild(dot, state.currentPath);
                state.paths.push(dot);
            } else {
                state.paths.push(state.currentPath);
            }
            state.currentPath = null;
            state.currentPoints = [];
            const count = state.paths.length;
            console.log(`[SVGCanvas] endStroke: stroke committed. Total strokes: ${count}. Notifying .NET...`);
            setUndoDisabled(container, false);
            state.dotNetRef.invokeMethodAsync('OnStrokeCompleted', count)
                .then(() => console.log(`[SVGCanvas] endStroke: OnStrokeCompleted(.NET) returned successfully.`))
                .catch((err) => console.error(`[SVGCanvas] endStroke: OnStrokeCompleted(.NET) call failed.`, err));
        }
    }

    const container = svg.closest('.svg-drawing-canvas');
    console.log(`[SVGCanvas] initialize: container element:`, container);

    // Undo button starts disabled; drawing enables it; undo to zero re-disables it.
    setUndoDisabled(container, true);

    const colorInput = container?.querySelector('.toolbar-color');
    console.log(`[SVGCanvas] initialize: colorInput element:`, colorInput);
    if (colorInput) {
        colorInput.addEventListener('input', (e) => {
            console.log(`[SVGCanvas] colorInput 'input' event: value="${e.target.value}". Updating state.color and notifying .NET...`);
            state.color = e.target.value;
            updateSwatchActive(container, e.target.value);
            state.dotNetRef.invokeMethodAsync('OnColorChanged', e.target.value)
                .then(() => console.log(`[SVGCanvas] colorInput: OnColorChanged(.NET) returned successfully.`))
                .catch((err) => console.error(`[SVGCanvas] colorInput: OnColorChanged(.NET) call failed.`, err));
        });
    } else {
        console.warn(`[SVGCanvas] initialize: colorInput (.toolbar-color) not found inside container. Color picker will not work.`);
    }

    const sizeInput = container?.querySelector('.toolbar-size');
    const sizeLabel = sizeInput?.closest('.toolbar-group')?.querySelector('.toolbar-label');
    console.log(`[SVGCanvas] initialize: sizeInput element:`, sizeInput);
    if (sizeInput) {
        sizeInput.addEventListener('input', (e) => {
            const width = parseFloat(e.target.value);
            console.log(`[SVGCanvas] sizeInput 'input' event: raw="${e.target.value}", parsed=${width}`);
            if (!isNaN(width)) {
                state.strokeWidth = width;
                if (sizeLabel) sizeLabel.textContent = `Size: ${width}`;
                state.dotNetRef.invokeMethodAsync('OnStrokeWidthChanged', width)
                    .then(() => console.log(`[SVGCanvas] sizeInput: OnStrokeWidthChanged(.NET) returned successfully.`))
                    .catch((err) => console.error(`[SVGCanvas] sizeInput: OnStrokeWidthChanged(.NET) call failed.`, err));
            } else {
                console.warn(`[SVGCanvas] sizeInput: parsed width is NaN, ignoring.`);
            }
        });
    } else {
        console.warn(`[SVGCanvas] initialize: sizeInput (.toolbar-size) not found inside container. Brush size slider will not work.`);
    }

    // Toolbar event delegation — handles swatch clicks, undo, and export entirely in JS.
    // This bypasses Blazor's @onclick system, which is unreliable for components in RCLs
    // when the render mode is applied at the Routes level rather than per-component.
    if (container) {
        container.addEventListener('click', (e) => {
            // ── Swatch click ──────────────────────────────────────────────────────────
            const swatchEl = e.target.closest('.toolbar-swatch[data-color]');
            if (swatchEl) {
                const color = swatchEl.dataset.color;
                console.log(`[SVGCanvas] Swatch clicked: color="${color}"`);
                state.color = color;
                if (colorInput) colorInput.value = color;
                updateSwatchActive(container, color);
                state.dotNetRef.invokeMethodAsync('OnColorChanged', color)
                    .then(() => console.log(`[SVGCanvas] Swatch: OnColorChanged(.NET) returned.`))
                    .catch(err => console.error('[SVGCanvas] Swatch: OnColorChanged(.NET) failed.', err));
                return;
            }

            // ── Undo button click ─────────────────────────────────────────────────────
            const undoBtn = e.target.closest('.toolbar-btn-undo');
            if (undoBtn) {
                if (undoBtn.classList.contains('toolbar-btn-disabled')) {
                    console.log(`[SVGCanvas] Undo clicked but disabled (no strokes to undo).`);
                    return;
                }
                console.log(`[SVGCanvas] Undo clicked. Stroke count: ${state.paths.length}`);
                if (state.paths.length === 0) return;
                const last = state.paths.pop();
                last.remove();
                const remaining = state.paths.length;
                console.log(`[SVGCanvas] Undo complete. Remaining strokes: ${remaining}`);
                setUndoDisabled(container, remaining === 0);
                state.dotNetRef.invokeMethodAsync('OnStrokeCompleted', remaining)
                    .then(() => console.log(`[SVGCanvas] Undo: OnStrokeCompleted(.NET) returned.`))
                    .catch(err => console.error('[SVGCanvas] Undo: OnStrokeCompleted(.NET) failed.', err));
                return;
            }

            // ── Export SVG button click ───────────────────────────────────────────────
            if (e.target.closest('.toolbar-btn-export')) {
                console.log(`[SVGCanvas] Export SVG clicked.`);
                const svgEl = state.svg;
                const rect = svgEl.getBoundingClientRect();
                const width = Math.round(rect.width);
                const height = Math.round(rect.height);
                console.log(`[SVGCanvas] Export: SVG bounding rect — width=${width}, height=${height}`);
                const clone = svgEl.cloneNode(true);
                clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
                clone.setAttribute('width', width);
                clone.setAttribute('height', height);
                clone.setAttribute('viewBox', `0 0 ${width} ${height}`);
                const bg = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
                bg.setAttribute('width', '100%');
                bg.setAttribute('height', '100%');
                bg.setAttribute('fill', state.backgroundColor);
                clone.insertBefore(bg, clone.firstChild);
                const content = new XMLSerializer().serializeToString(clone);
                console.log(`[SVGCanvas] Export: serialized SVG length = ${content.length} chars`);
                const blob = new Blob([content], { type: 'image/svg+xml;charset=utf-8' });
                const url = URL.createObjectURL(blob);
                const now = new Date();
                const pad = n => String(n).padStart(2, '0');
                const ts = `${now.getUTCFullYear()}${pad(now.getUTCMonth() + 1)}${pad(now.getUTCDate())}-${pad(now.getUTCHours())}${pad(now.getUTCMinutes())}${pad(now.getUTCSeconds())}`;
                const a = document.createElement('a');
                a.href = url;
                a.download = `drawing-${ts}.svg`;
                document.body.appendChild(a);
                a.click();
                setTimeout(() => {
                    document.body.removeChild(a);
                    URL.revokeObjectURL(url);
                    console.log(`[SVGCanvas] Export: blob URL revoked and anchor removed.`);
                }, 100);
                return;
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
        const touch = e.touches[0];
        startStroke(touch.clientX, touch.clientY);
    }, { passive: false });

    svg.addEventListener('touchmove', (e) => {
        e.preventDefault();
        const touch = e.touches[0];
        continueStroke(touch.clientX, touch.clientY);
    }, { passive: false });

    svg.addEventListener('touchend', (e) => {
        e.preventDefault();
        endStroke();
    }, { passive: false });

    console.log(`[SVGCanvas] initialize: all event listeners attached. Canvas is ready.`);
}

function buildPath(points) {
    if (points.length === 1) {
        return `M ${points[0].x} ${points[0].y}`;
    }
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
 * Updates the current stroke color.
 * @param {string} svgId
 * @param {string} color
 */
export function setColor(svgId, color) {
    console.log(`[SVGCanvas] setColor called — svgId="${svgId}", color="${color}"`);
    const state = instances.get(svgId);
    if (!state) {
        console.warn(`[SVGCanvas] setColor: no state found for svgId="${svgId}".`);
        return;
    }
    state.color = color;
    const container = state.svg.closest('.svg-drawing-canvas');
    const colorInput = container?.querySelector('.toolbar-color');
    if (colorInput) {
        colorInput.value = color;
        console.log(`[SVGCanvas] setColor: colorInput synced to "${color}".`);
    } else {
        console.warn(`[SVGCanvas] setColor: colorInput not found, could not sync picker UI.`);
    }
    updateSwatchActive(container, color);
}

/**
 * Updates the current stroke width.
 * @param {string} svgId
 * @param {number} width
 */
export function setStrokeWidth(svgId, width) {
    console.log(`[SVGCanvas] setStrokeWidth called — svgId="${svgId}", width=${width}`);
    const state = instances.get(svgId);
    if (!state) {
        console.warn(`[SVGCanvas] setStrokeWidth: no state found for svgId="${svgId}".`);
        return;
    }
    state.strokeWidth = width;
}

/**
 * Removes the most recently drawn stroke. Returns the remaining stroke count.
 * @param {string} svgId
 * @returns {number}
 */
export function undo(svgId) {
    console.log(`[SVGCanvas] undo called — svgId="${svgId}"`);
    const state = instances.get(svgId);
    if (!state) {
        console.warn(`[SVGCanvas] undo: no state found for svgId="${svgId}".`);
        return 0;
    }
    console.log(`[SVGCanvas] undo: current stroke count = ${state.paths.length}`);
    if (state.paths.length === 0) {
        console.log(`[SVGCanvas] undo: nothing to undo.`);
        return 0;
    }
    const last = state.paths.pop();
    last.remove();
    const remaining = state.paths.length;
    console.log(`[SVGCanvas] undo: stroke removed. Remaining stroke count = ${remaining}`);
    setUndoDisabled(state.svg.closest('.svg-drawing-canvas'), remaining === 0);
    return remaining;
}

/**
 * Removes all strokes from the canvas.
 * @param {string} svgId
 */
export function clear(svgId) {
    console.log(`[SVGCanvas] clear called — svgId="${svgId}"`);
    const state = instances.get(svgId);
    if (!state) {
        console.warn(`[SVGCanvas] clear: no state found for svgId="${svgId}".`);
        return;
    }
    console.log(`[SVGCanvas] clear: removing ${state.paths.length} stroke(s).`);
    for (const path of state.paths) {
        path.remove();
    }
    state.paths = [];
    setUndoDisabled(state.svg.closest('.svg-drawing-canvas'), true);
    console.log(`[SVGCanvas] clear: done.`);
}

/**
 * Serializes the current SVG drawing to a string and triggers a file download.
 * @param {string} svgId
 * @param {string} fileName
 * @param {string} backgroundColor
 */
export function downloadSvg(svgId, fileName, backgroundColor) {
    console.log(`[SVGCanvas] downloadSvg called — svgId="${svgId}", fileName="${fileName}", backgroundColor="${backgroundColor}"`);
    const state = instances.get(svgId);
    if (!state) {
        console.warn(`[SVGCanvas] downloadSvg: no state found for svgId="${svgId}".`);
        return;
    }

    const svgEl = state.svg;
    const rect = svgEl.getBoundingClientRect();
    const width = Math.round(rect.width);
    const height = Math.round(rect.height);
    console.log(`[SVGCanvas] downloadSvg: SVG bounding rect — width=${width}, height=${height}`);

    const clone = svgEl.cloneNode(true);
    clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
    clone.setAttribute('width', width);
    clone.setAttribute('height', height);
    clone.setAttribute('viewBox', `0 0 ${width} ${height}`);

    // Insert background rect as first child so it renders behind all strokes
    const bg = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
    bg.setAttribute('width', '100%');
    bg.setAttribute('height', '100%');
    bg.setAttribute('fill', backgroundColor || 'white');
    clone.insertBefore(bg, clone.firstChild);

    const content = new XMLSerializer().serializeToString(clone);
    console.log(`[SVGCanvas] downloadSvg: serialized SVG length = ${content.length} chars`);

    const blob = new Blob([content], { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    console.log(`[SVGCanvas] downloadSvg: blob URL created — "${url}". Triggering download...`);

    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    setTimeout(() => {
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
        console.log(`[SVGCanvas] downloadSvg: blob URL revoked and anchor removed.`);
    }, 100);
}

/**
 * Cleans up event listeners and instance state for the given SVG element.
 * @param {string} svgId
 */
export function dispose(svgId) {
    console.log(`[SVGCanvas] dispose called — svgId="${svgId}"`);
    instances.delete(svgId);
    console.log(`[SVGCanvas] dispose: instance removed. Remaining instances: ${instances.size}`);
}
