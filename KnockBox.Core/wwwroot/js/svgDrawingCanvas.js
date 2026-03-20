const instances = new Map();

/**
 * Initializes the SVG drawing canvas for a given element ID.
 * @param {string} svgId - The ID of the SVG element.
 * @param {object} dotNetRef - The .NET object reference for JS-to-.NET callbacks.
 * @param {string} initialColor - The initial stroke color.
 * @param {number} initialStrokeWidth - The initial stroke width in pixels.
 */
export function initialize(svgId, dotNetRef, initialColor, initialStrokeWidth) {
    const svg = document.getElementById(svgId);
    if (!svg) return;

    const state = {
        svg,
        dotNetRef,
        color: initialColor,
        strokeWidth: initialStrokeWidth,
        isDrawing: false,
        currentPath: null,
        currentPoints: [],
        paths: [],
    };

    instances.set(svgId, state);

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
            state.dotNetRef.invokeMethodAsync('OnStrokeCompleted', state.paths.length)
                .catch(() => { /* Component may have been disposed; ignore. */ });
        }
    }

    const container = svg.closest('.svg-drawing-canvas');

    const colorInput = container?.querySelector('.toolbar-color');
    if (colorInput) {
        colorInput.addEventListener('input', (e) => {
            state.color = e.target.value;
            state.dotNetRef.invokeMethodAsync('OnColorChanged', e.target.value)
                .catch(() => { /* Component may have been disposed; ignore. */ });
        });
    }

    const sizeInput = container?.querySelector('.toolbar-size');
    if (sizeInput) {
        sizeInput.addEventListener('input', (e) => {
            const width = parseFloat(e.target.value);
            if (!isNaN(width)) {
                state.strokeWidth = width;
                state.dotNetRef.invokeMethodAsync('OnStrokeWidthChanged', width)
                    .catch(() => { /* Component may have been disposed; ignore. */ });
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
    const state = instances.get(svgId);
    if (state) {
        state.color = color;
        const colorInput = state.svg.closest('.svg-drawing-canvas')?.querySelector('.toolbar-color');
        if (colorInput) colorInput.value = color;
    }
}

/**
 * Updates the current stroke width.
 * @param {string} svgId
 * @param {number} width
 */
export function setStrokeWidth(svgId, width) {
    const state = instances.get(svgId);
    if (state) state.strokeWidth = width;
}

/**
 * Removes the most recently drawn stroke. Returns the remaining stroke count.
 * @param {string} svgId
 * @returns {number}
 */
export function undo(svgId) {
    const state = instances.get(svgId);
    if (!state || state.paths.length === 0) return 0;
    const last = state.paths.pop();
    last.remove();
    return state.paths.length;
}

/**
 * Removes all strokes from the canvas.
 * @param {string} svgId
 */
export function clear(svgId) {
    const state = instances.get(svgId);
    if (!state) return;
    for (const path of state.paths) {
        path.remove();
    }
    state.paths = [];
}

/**
 * Serializes the current SVG drawing to a string and triggers a file download.
 * @param {string} svgId
 * @param {string} fileName
 * @param {string} backgroundColor
 */
export function downloadSvg(svgId, fileName, backgroundColor) {
    const state = instances.get(svgId);
    if (!state) return;

    const svgEl = state.svg;
    const rect = svgEl.getBoundingClientRect();
    const width = Math.round(rect.width);
    const height = Math.round(rect.height);

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
    const blob = new Blob([content], { type: 'image/svg+xml;charset=utf-8' });
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
 * Cleans up event listeners and instance state for the given SVG element.
 * @param {string} svgId
 */
export function dispose(svgId) {
    instances.delete(svgId);
}
