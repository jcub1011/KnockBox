const instances = new Map();

/**
 * Converts a CSS rgb(r, g, b) string to a hex color string.
 * @param {string} rgb - e.g. "rgb(255, 0, 128)"
 * @returns {string|null} hex string e.g. "#ff0080", or null if parsing fails
 */
function rgbToHex(rgb) {
    const match = rgb?.match(/rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/);
    if (!match) return null;
    return '#' + [match[1], match[2], match[3]]
        .map(n => parseInt(n, 10).toString(16).padStart(2, '0'))
        .join('');
}

/**
 * Checks if two 32-bit integer colors match within a given tolerance per channel.
 */
function colorsMatch(c1, c2, tolerance = 0) {
    if (c1 === c2) return true;
    if (tolerance === 0) return false;
    const r1 = (c1 >> 24) & 0xFF, g1 = (c1 >> 16) & 0xFF, b1 = (c1 >> 8) & 0xFF;
    const r2 = (c2 >> 24) & 0xFF, g2 = (c2 >> 16) & 0xFF, b2 = (c2 >> 8) & 0xFF;
    return Math.abs(r1 - r2) <= tolerance && 
           Math.abs(g1 - g2) <= tolerance && 
           Math.abs(b1 - b2) <= tolerance;
}

/**
 * Computes the triangle area formed by three {x, y} points using the cross-product formula.
 * Returns the absolute area (not halved) for efficiency — only relative ordering matters.
 * @param {{x: number, y: number}} a
 * @param {{x: number, y: number}} b
 * @param {{x: number, y: number}} c
 * @returns {number}
 */
function triangleArea(a, b, c) {
    return Math.abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y));
}

/**
 * Simplifies an array of {x, y} points using the Visvalingam-Whyatt algorithm.
 *
 * The algorithm iteratively removes the point whose removal causes the least
 * change in shape (measured by the area of the triangle it forms with its
 * neighbors). This preserves high-curvature features while aggressively
 * pruning near-collinear runs, making it well-suited for freehand drawings.
 *
 * @param {{x: number, y: number}[]} points - Input polyline (not mutated).
 * @param {number} [minArea=2] - Minimum effective area threshold. Points whose
 *     triangle area is below this value are candidates for removal. Higher
 *     values yield more aggressive simplification. Reasonable range for
 *     screen-space SVG coordinates: 0.5 (subtle) to 8 (aggressive).
 * @param {number} [minPoints=3] - Never reduce below this many points.
 *     Defaults to 3 so that a stroke always retains start, middle, and end.
 * @returns {{x: number, y: number}[]} A new array with a subset of the original points.
 */
function visvalingamWhyatt(points, minArea = 2, minPoints = 3) {
    const len = points.length;
    if (len <= minPoints) return points.slice();

    // Build a doubly-linked list so removals are O(1).
    const nodes = points.map((p, i) => ({ x: p.x, y: p.y, index: i, prev: null, next: null, area: Infinity }));
    for (let i = 0; i < len; i++) {
        nodes[i].prev = nodes[i - 1] || null;
        nodes[i].next = nodes[i + 1] || null;
    }

    // Compute initial effective areas (endpoints keep Infinity so they're never removed).
    for (let i = 1; i < len - 1; i++) {
        nodes[i].area = triangleArea(nodes[i].prev, nodes[i], nodes[i].next);
    }

    // Use a simple min-scan approach. For typical freehand stroke sizes (hundreds of
    // points at most) this is fast enough and avoids the complexity of a binary heap
    // that must support key-decrease operations.
    let remaining = len;

    while (remaining > minPoints) {
        // Find the interior node with the smallest area.
        let minNode = null;
        let minVal = Infinity;
        let cur = nodes[0].next; // skip first endpoint
        while (cur && cur.next) { // skip last endpoint
            if (cur.area < minVal) {
                minVal = cur.area;
                minNode = cur;
            }
            cur = cur.next;
        }

        if (!minNode || minVal >= minArea) break; // All remaining points are significant.

        // Remove the node from the linked list.
        minNode.prev.next = minNode.next;
        minNode.next.prev = minNode.prev;
        remaining--;

        // Recalculate areas for the two neighbors that are now adjacent to each other.
        const prev = minNode.prev;
        const next = minNode.next;
        if (prev.prev) {
            prev.area = Math.max(triangleArea(prev.prev, prev, next), minVal);
        }
        if (next.next) {
            next.area = Math.max(triangleArea(prev, next, next.next), minVal);
        }
    }

    // Walk the surviving linked list to build the result.
    const result = [];
    let node = nodes[0];
    while (node) {
        result.push({ x: node.x, y: node.y });
        node = node.next;
    }
    return result;
}

/**
 * Computes a 32-bit integer representation of a pixel's RGBA color.
 */
function getPixelColor(data, x, y, width) {
    const idx = (y * width + x) * 4;
    return (data[idx] << 24) | (data[idx + 1] << 16) | (data[idx + 2] << 8) | data[idx + 3];
}

/**
 * Performs a span-based flood fill on ImageData, returning a bitmask of the filled area.
 */
function floodFillSpan(imageData, x, y, fillColor) {
    const data = imageData.data;
    const width = imageData.width;
    const height = imageData.height;
    
    const targetColor = getPixelColor(data, x, y, width);
    
    // Parse hex fill color to 32-bit int
    const fillR = parseInt(fillColor.slice(1, 3), 16);
    const fillG = parseInt(fillColor.slice(3, 5), 16);
    const fillB = parseInt(fillColor.slice(5, 7), 16);
    const fillIntValue = (fillR << 24) | (fillG << 16) | (fillB << 8) | 255;

    if (targetColor === fillIntValue) return null;

    const mask = new Uint8Array(width * height);
    const stack = [[x, y]];

    while (stack.length > 0) {
        let [lx, ly] = stack.pop();
        let rx = lx;
        
        while (lx > 0 && getPixelColor(data, lx - 1, ly, width) === targetColor && !mask[ly * width + (lx - 1)]) {
            lx--;
        }
        while (rx < width - 1 && getPixelColor(data, rx + 1, ly, width) === targetColor && !mask[ly * width + (rx + 1)]) {
            rx++;
        }

        for (let i = lx; i <= rx; i++) {
            mask[ly * width + i] = 1;
            if (ly > 0 && getPixelColor(data, i, ly - 1, width) === targetColor && !mask[(ly - 1) * width + i]) {
                if (i === lx || getPixelColor(data, i - 1, ly - 1, width) !== targetColor || mask[(ly - 1) * width + (i - 1)]) {
                    stack.push([i, ly - 1]);
                }
            }
            if (ly < height - 1 && getPixelColor(data, i, ly + 1, width) === targetColor && !mask[(ly + 1) * width + i]) {
                if (i === lx || getPixelColor(data, i - 1, ly + 1, width) !== targetColor || mask[(ly + 1) * width + (i - 1)]) {
                    stack.push([i, ly + 1]);
                }
            }
        }
    }
    return mask;
}

/**
 * Converts a bitmask to an SVG path string consisting of horizontal spans.
 * @param {Uint8Array} mask
 * @param {number} width - mask width in pixels
 * @param {number} height - mask height in pixels
 * @param {number} scale - scale factor used during rasterization
 */
function maskToPath(mask, width, height, scale = 1) {
    const paths = [];
    const s = 1 / scale;
    const r = n => Math.round(n * 100) / 100;

    for (let y = 0; y < height; y++) {
        let startX = -1;
        for (let x = 0; x <= width; x++) {
            const isFilled = x < width && mask[y * width + x];
            if (isFilled && startX === -1) {
                startX = x;
            } else if (!isFilled && startX !== -1) {
                const rx = startX * s;
                const ry = y * s;
                const rw = (x - startX) * s;
                const rh = s;
                paths.push(`M${r(rx)} ${r(ry)}h${r(rw)}v${r(rh)}h${r(-rw)}z`);
                startX = -1;
            }
        }
    }
    return paths.join('');
}

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
 * Sets or clears the visual disabled state on the redo button.
 * @param {Element|null} container
 * @param {boolean} disabled
 */
function setRedoDisabled(container, disabled) {
    container?.querySelector('.toolbar-btn-redo')
        ?.classList.toggle('toolbar-btn-disabled', disabled);
}

/**
 * Updates which swatch button bears the active highlight.
 * @param {Element|null} container
 * @param {string} color - hex or CSS color string
 */
function updateSwatchActive(container, color) {
    const customSwatch = container?.querySelector('.toolbar-swatch-custom');
    let matchedPreset = false;
    container?.querySelectorAll('.toolbar-swatch[data-color]:not(.toolbar-swatch-custom)').forEach(s => {
        const isMatch = s.dataset.color.toLowerCase() === color.toLowerCase();
        if (isMatch) matchedPreset = true;
        s.classList.toggle('toolbar-swatch-active', isMatch);
    });
    if (customSwatch) {
        if (!matchedPreset) {
            customSwatch.style.backgroundColor = color;
            customSwatch.classList.add('toolbar-swatch-active');
        } else {
            customSwatch.classList.remove('toolbar-swatch-active');
        }
    }
}

/**
 * Updates which size preset button bears the active highlight.
 * @param {Element|null} container
 * @param {number} size - stroke width in pixels
 */
function updateSizeActive(container, size) {
    container?.querySelectorAll('.toolbar-size-btn[data-size]').forEach(s => {
        s.classList.toggle('toolbar-size-btn-active',
            parseInt(s.dataset.size, 10) === size);
    });
}

/**
 * Builds a smooth quadratic Bézier path string from an array of {x, y} points.
 * @param {{x: number, y: number}[]} points
 * @returns {string}
 */
function buildPath(points) {
    const r = n => Math.round(n * 100) / 100;
    if (points.length === 1) return `M ${r(points[0].x)} ${r(points[0].y)}`;
    const parts = [`M ${r(points[0].x)} ${r(points[0].y)}`];
    for (let i = 1; i < points.length - 1; i++) {
        const mx = r((points[i].x + points[i + 1].x) / 2);
        const my = r((points[i].y + points[i + 1].y) / 2);
        parts.push(`Q ${r(points[i].x)} ${r(points[i].y)} ${mx} ${my}`);
    }
    const last = points[points.length - 1];
    parts.push(`L ${r(last.x)} ${r(last.y)}`);
    return parts.join(' ');
}

/**
 * Clones the SVG, injects a background rect, and triggers a browser file download.
 * @param {object} state - Canvas instance state.
 * @param {string} fileName - Download filename.
 * @param {string} [backgroundColor] - Overrides state.backgroundColor when provided.
 */
function triggerSvgDownload(state, fileName, backgroundColor) {
    const svgEl = state.svg;
    const vb = svgEl.viewBox?.baseVal;
    const hasViewBox = vb && vb.width > 0 && vb.height > 0;
    const width = hasViewBox ? vb.width : Math.round(svgEl.getBoundingClientRect().width);
    const height = hasViewBox ? vb.height : Math.round(svgEl.getBoundingClientRect().height);

    const clone = svgEl.cloneNode(true);

    // Strip DOM-only attributes (Blazor CSS isolation, class, id, inline style) that
    // are meaningless in a standalone SVG file and can confuse basic renderers (e.g.
    // Windows thumbnail generators).
    clone.removeAttribute('id');
    clone.removeAttribute('class');
    clone.removeAttribute('style');
    for (const attr of [...clone.attributes]) {
        if (attr.name.startsWith('b-')) clone.removeAttribute(attr.name);
    }

    clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
    clone.setAttribute('width', width);
    clone.setAttribute('height', height);
    clone.setAttribute('viewBox', `0 0 ${width} ${height}`);

    // Insert background rect as the first child so it renders behind all strokes.
    const bg = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
    bg.setAttribute('width', width);
    bg.setAttribute('height', height);
    bg.setAttribute('fill', backgroundColor || state.svg.style.backgroundColor || state.backgroundColor);
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

    const abortController = new AbortController();
    const signal = abortController.signal;

    const state = {
        svg,
        dotNetRef,
        abortController,
        color: initialColor,
        strokeWidth: initialStrokeWidth,
        backgroundColor: initialBackgroundColor || 'white',
        isDrawing: false,
        currentPath: null,
        currentPoints: [],
        paths: [],
        /** Visvalingam-Whyatt minimum area threshold for stroke simplification.
         *  0 = no simplification; 0.5 = subtle; 2 = balanced (default); 8 = aggressive. */
        simplifyMinArea: 2,
        currentTool: 'brush', // 'brush', 'eraser', or 'fill'
        undoStack: [], // Stores actions: { type: 'draw'|'erase'|'clear', element/elements, index }
        redoStack: [],
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

    function eraseAt(clientX, clientY) {
        const el = document.elementFromPoint(clientX, clientY);
        // Only erase paths/circles that belong to THIS SVG.
        if (el && (el.tagName === 'path' || el.tagName === 'circle') && el.parentNode === svg) {
            const idx = state.paths.indexOf(el);
            if (idx !== -1) {
                state.paths.splice(idx, 1);
                el.remove();
                state.undoStack.push({ type: 'erase', element: el, index: idx });
                state.redoStack = [];
                setUndoDisabled(container, false);
                setRedoDisabled(container, true);
                state.dotNetRef.invokeMethodAsync('OnStrokeCompleted', state.paths.length)
                    .catch(err => console.error('[SVGCanvas] OnStrokeCompleted failed.', err));
            }
        }
    }

    function performFloodFillAt(clientX, clientY) {
        const { x, y } = getSvgCoords(clientX, clientY);
        const SCALE = 2; // 2x resolution for better precision and smoother edges
        
        // Get coordinate space dimensions from viewBox or client bounds.
        const vb = svg.viewBox?.baseVal;
        const viewWidth = (vb && vb.width > 0) ? vb.width : svg.clientWidth;
        const viewHeight = (vb && vb.height > 0) ? vb.height : svg.clientHeight;
        
        const width = Math.round(viewWidth * SCALE);
        const height = Math.round(viewHeight * SCALE);
        
        // Rasterize current state to a temporary canvas for flood fill calculation.
        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const ctx = canvas.getContext('2d', { willReadFrequently: true });
        
        ctx.scale(SCALE, SCALE);

        // Background
        ctx.fillStyle = svg.style.backgroundColor || state.backgroundColor;
        ctx.fillRect(0, 0, viewWidth, viewHeight);
        
        // Draw existing strokes (only those tracked in state.paths)
        for (const el of state.paths) {
            ctx.strokeStyle = el.getAttribute('stroke') || 'none';
            ctx.lineWidth = el.getAttribute('stroke-width') || 0;
            ctx.lineCap = el.getAttribute('stroke-linecap') || 'round';
            ctx.lineJoin = el.getAttribute('stroke-linejoin') || 'round';
            ctx.fillStyle = el.getAttribute('fill') || 'none';
            
            if (el.tagName === 'path') {
                const fillRule = el.getAttribute('fill-rule') || 'nonzero';
                const p = new Path2D(el.getAttribute('d'));
                if (ctx.fillStyle !== 'none') ctx.fill(p, fillRule);
                
                // Fills should NOT be stroked during rasterization to prevent bleed-over
                // into adjacent empty areas (which would block subsequent fills).
                if (el.getAttribute('data-type') !== 'fill') {
                    if (ctx.strokeStyle !== 'none') ctx.stroke(p);
                }
            } else if (el.tagName === 'circle') {
                ctx.beginPath();
                ctx.arc(parseFloat(el.getAttribute('cx')), parseFloat(el.getAttribute('cy')), parseFloat(el.getAttribute('r')), 0, Math.PI * 2);
                if (ctx.fillStyle !== 'none') ctx.fill();
                if (ctx.strokeStyle !== 'none') ctx.stroke();
            }
        }
        
        const imageData = ctx.getImageData(0, 0, width, height);
        const ix = Math.round(x * SCALE);
        const iy = Math.round(y * SCALE);
        
        if (ix < 0 || ix >= width || iy < 0 || iy >= height) return;
        
        const targetColor = getPixelColor(imageData.data, ix, iy, width);
        const fillR = parseInt(state.color.slice(1, 3), 16);
        const fillG = parseInt(state.color.slice(3, 5), 16);
        const fillB = parseInt(state.color.slice(5, 7), 16);
        const fillIntValue = (fillR << 24) | (fillG << 16) | (fillB << 8) | 255;

        // Tolerance check for the seed point to avoid "refusing" to fill due to sub-pixel bleed
        if (colorsMatch(targetColor, fillIntValue, 2)) return;

        const mask = floodFillSpan(imageData, ix, iy, state.color);
        if (!mask) return; 
        
        const d = maskToPath(mask, width, height, SCALE);
        if (!d) return;
        
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('data-type', 'fill');
        path.setAttribute('fill', state.color);
        path.setAttribute('stroke', state.color);
        path.setAttribute('stroke-width', '1'); // Reduced bleed
        path.setAttribute('stroke-linejoin', 'round');
        path.setAttribute('d', d);
        
        // Insertion logic: Keep fills behind all strokes, but newer fills on top of older fills.
        let lastFillIdx = -1;
        for (let i = 0; i < state.paths.length; i++) {
            if (state.paths[i].getAttribute('data-type') === 'fill') {
                lastFillIdx = i;
            } else {
                break; // Fills always come first in the path list
            }
        }
        
        const insertionIdx = lastFillIdx + 1;
        const nextElInDom = state.paths[insertionIdx];
        svg.insertBefore(path, nextElInDom || null);
        state.paths.splice(insertionIdx, 0, path);
        
        state.undoStack.push({ type: 'draw', element: path });
        state.redoStack = [];
        setUndoDisabled(container, false);
        setRedoDisabled(container, true);
        state.dotNetRef.invokeMethodAsync('OnStrokeCompleted', state.paths.length)
            .catch(err => console.error('[SVGCanvas] OnStrokeCompleted failed.', err));
    }

    function startStroke(clientX, clientY) {
        if (state.currentTool === 'eraser') {
            state.isDrawing = true;
            state.isErasing = true;
            eraseAt(clientX, clientY);
            return;
        }
        if (state.currentTool === 'fill') {
            performFloodFillAt(clientX, clientY);
            return;
        }
        state.isDrawing = true;
        const { x, y } = getSvgCoords(clientX, clientY);
        state.currentPoints = [{ x, y }];
        const r = n => Math.round(n * 100) / 100;
        state._pathPrefix = `M ${r(x)} ${r(y)}`;
        state._pathSuffix = '';
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('stroke', state.color);
        path.setAttribute('stroke-width', state.strokeWidth);
        path.setAttribute('fill', 'none');
        path.setAttribute('stroke-linecap', 'round');
        path.setAttribute('stroke-linejoin', 'round');
        path.setAttribute('d', state._pathPrefix);
        svg.appendChild(path);
        state.currentPath = path;
    }

    function continueStroke(clientX, clientY) {
        if (!state.isDrawing) return;
        if (state.isErasing) {
            eraseAt(clientX, clientY);
            return;
        }
        if (!state.currentPath) return;
        const { x, y } = getSvgCoords(clientX, clientY);

        const pts = state.currentPoints;
        const last = pts[pts.length - 1];
        const dx = x - last.x;
        const dy = y - last.y;
        if (dx * dx + dy * dy < 4) return;

        pts.push({ x, y });
        const n = pts.length;

        const r = n => Math.round(n * 100) / 100;
        if (n === 2) {
            state._pathSuffix = ` L ${r(x)} ${r(y)}`;
        } else {
            const prev = pts[n - 2];
            const mx = r((prev.x + x) / 2);
            const my = r((prev.y + y) / 2);
            state._pathPrefix += ` Q ${r(prev.x)} ${r(prev.y)} ${mx} ${my}`;
            state._pathSuffix = ` L ${r(x)} ${r(y)}`;
        }

        // Batch DOM updates via requestAnimationFrame for smooth 60fps rendering.
        if (!state._rafPending) {
            state._rafPending = true;
            requestAnimationFrame(() => {
                if (state.currentPath) {
                    state.currentPath.setAttribute('d', state._pathPrefix + state._pathSuffix);
                }
                state._rafPending = false;
            });
        }
    }

    function endStroke() {
        if (!state.isDrawing) return;
        state.isDrawing = false;
        state.isErasing = false;
        if (!state.currentPath) return;

        // Flush any pending rAF so the final stroke segment is not lost.
        if (state._rafPending && state.currentPath) {
            state.currentPath.setAttribute('d', state._pathPrefix + state._pathSuffix);
            state._rafPending = false;
        }

        let element;
        if (state.currentPoints.length === 1) {
            // Single tap/click — render as a small filled circle.
            const { x, y } = state.currentPoints[0];
            const dot = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
            dot.setAttribute('cx', Math.round(x * 100) / 100);
            dot.setAttribute('cy', Math.round(y * 100) / 100);
            dot.setAttribute('r', Math.round(state.strokeWidth / 2 * 100) / 100);
            dot.setAttribute('fill', state.color);
            svg.replaceChild(dot, state.currentPath);
            element = dot;
        } else {
            // Simplify the stroke using Visvalingam-Whyatt before committing.
            const simplified = visvalingamWhyatt(
                state.currentPoints,
                state.simplifyMinArea ?? 2,
                3
            );
            state.currentPath.setAttribute('d', buildPath(simplified));
            element = state.currentPath;
        }

        state.paths.push(element);
        state.undoStack.push({ type: 'draw', element });
        state.redoStack = [];
        state.currentPath = null;
        state.currentPoints = [];
        setUndoDisabled(container, false);
        setRedoDisabled(container, true);
        state.dotNetRef.invokeMethodAsync('OnStrokeCompleted', state.paths.length)
            .catch(err => console.error('[SVGCanvas] OnStrokeCompleted failed.', err));
    }

    // Undo/Redo starts disabled
    setUndoDisabled(container, true);
    setRedoDisabled(container, true);

    function deselectToolsIfActive() {
        if (state.currentTool !== 'brush') {
            state.currentTool = 'brush';
            container.querySelector('.toolbar-btn-eraser')?.classList.remove('toolbar-btn-active');
            container.querySelector('.toolbar-btn-fill')?.classList.remove('toolbar-btn-active');
            state.dotNetRef.invokeMethodAsync('OnToolChanged', 'brush')
                .catch(err => console.error('[SVGCanvas] OnToolChanged failed.', err));
        }
    }

    // Color picker
    const colorInput = container?.querySelector('.toolbar-color');
    if (colorInput) {
        colorInput.addEventListener('input', (e) => {
            deselectToolsIfActive();
            state.color = e.target.value;
            updateSwatchActive(container, e.target.value);
            state.dotNetRef.invokeMethodAsync('OnColorChanged', e.target.value)
                .catch(err => console.error('[SVGCanvas] OnColorChanged failed.', err));
        }, { signal });
    } else {
        console.warn('[SVGCanvas] initialize: .toolbar-color not found — color picker will not work.');
    }

    // Size input (custom number)
    const sizeInput = container?.querySelector('.toolbar-size');
    if (sizeInput) {
        sizeInput.addEventListener('change', (e) => {
            let width = parseInt(e.target.value, 10);
            if (isNaN(width) || width < 1) width = 1;
            if (width > 30) width = 30;
            e.target.value = width;
            state.strokeWidth = width;
            updateSizeActive(container, width);
            state.dotNetRef.invokeMethodAsync('OnStrokeWidthChanged', width)
                .catch(err => console.error('[SVGCanvas] OnStrokeWidthChanged failed.', err));
        }, { signal });
    }

    // Delegated click handler for swatches, undo, and export.
    if (container) {
        container.addEventListener('click', (e) => {
            // Custom swatch — select it with its current color
            const customSwatchEl = e.target.closest('.toolbar-swatch-custom');
            if (customSwatchEl) {
                deselectToolsIfActive();
                const rgb = getComputedStyle(customSwatchEl).backgroundColor;
                const hex = rgbToHex(rgb) || state.color;
                state.color = hex;
                if (colorInput) colorInput.value = hex;
                updateSwatchActive(container, hex);
                state.dotNetRef.invokeMethodAsync('OnColorChanged', hex)
                    .catch(err => console.error('[SVGCanvas] OnColorChanged failed.', err));
                return;
            }

            // Swatch
            const swatchEl = e.target.closest('.toolbar-swatch[data-color]');
            if (swatchEl) {
                deselectToolsIfActive();
                const color = swatchEl.dataset.color;
                state.color = color;
                if (colorInput) colorInput.value = color;
                updateSwatchActive(container, color);
                state.dotNetRef.invokeMethodAsync('OnColorChanged', color)
                    .catch(err => console.error('[SVGCanvas] OnColorChanged failed.', err));
                return;
            }

            // Size preset
            const sizeBtn = e.target.closest('.toolbar-size-btn[data-size]');
            if (sizeBtn) {
                const width = parseInt(sizeBtn.dataset.size, 10);
                state.strokeWidth = width;
                if (sizeInput) sizeInput.value = width;
                updateSizeActive(container, width);
                state.dotNetRef.invokeMethodAsync('OnStrokeWidthChanged', width)
                    .catch(err => console.error('[SVGCanvas] OnStrokeWidthChanged failed.', err));
                return;
            }

            // Fill background with current color (undoable)
            const fillBtn = e.target.closest('.toolbar-btn-fill');
            if (fillBtn) {
                state.currentTool = state.currentTool === 'fill' ? 'brush' : 'fill';
                fillBtn.classList.toggle('toolbar-btn-active', state.currentTool === 'fill');
                container.querySelector('.toolbar-btn-eraser')?.classList.remove('toolbar-btn-active');
                state.dotNetRef.invokeMethodAsync('OnToolChanged', state.currentTool)
                    .catch(err => console.error('[SVGCanvas] OnToolChanged failed.', err));
                return;
            }

            // Eraser toggle
            const eraserBtn = e.target.closest('.toolbar-btn-eraser');
            if (eraserBtn) {
                state.currentTool = state.currentTool === 'eraser' ? 'brush' : 'eraser';
                eraserBtn.classList.toggle('toolbar-btn-active', state.currentTool === 'eraser');
                container.querySelector('.toolbar-btn-fill')?.classList.remove('toolbar-btn-active');
                state.dotNetRef.invokeMethodAsync('OnToolChanged', state.currentTool)
                    .catch(err => console.error('[SVGCanvas] OnToolChanged failed.', err));
                return;
            }

            // Clear all
            if (e.target.closest('.toolbar-btn-clear')) {
                clear(svgId);
                state.dotNetRef.invokeMethodAsync('OnStrokeCompleted', 0)
                    .catch(err => console.error('[SVGCanvas] OnStrokeCompleted failed.', err));
                return;
            }

            // Undo
            const undoBtn = e.target.closest('.toolbar-btn-undo');
            if (undoBtn) {
                undo(svgId);
                state.dotNetRef.invokeMethodAsync('OnStrokeCompleted', state.paths.length)
                    .catch(err => console.error('[SVGCanvas] OnStrokeCompleted failed.', err));
                return;
            }

            // Redo
            const redoBtn = e.target.closest('.toolbar-btn-redo');
            if (redoBtn) {
                redo(svgId);
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
                return;
            }

            // Copy — serializes the drawing server-side, copies code to clipboard.
            if (e.target.closest('.toolbar-btn-copy')) {
                state.dotNetRef.invokeMethodAsync('OnCopyRequestedAsync')
                    .then(code => {
                        if (!code) return;
                        navigator.clipboard.writeText(code).then(() => {
                            const toast = container.querySelector('.toolbar-copy-toast');
                            if (toast) {
                                toast.textContent = 'Copied!';
                                toast.style.display = '';
                                // Restart animation by removing and re-adding the element.
                                const parent = toast.parentNode;
                                const next = toast.nextSibling;
                                parent.removeChild(toast);
                                parent.insertBefore(toast, next);
                                setTimeout(() => { toast.style.display = 'none'; }, 1500);
                            }
                        }).catch(() => {
                            // Fallback: show code in toast if clipboard not available.
                            const toast = container.querySelector('.toolbar-copy-toast');
                            if (toast) {
                                toast.textContent = code;
                                toast.style.display = '';
                                setTimeout(() => { toast.style.display = 'none'; }, 3000);
                            }
                        });
                    })
                    .catch(err => console.error('[SVGCanvas] Copy failed.', err));
                return;
            }

            // Paste — reads code from clipboard and auto-submits.
            if (e.target.closest('.toolbar-btn-paste')) {
                navigator.clipboard.readText().then(text => {
                    const code = (text || '').trim().toUpperCase();
                    if (!code) return;
                    state.dotNetRef.invokeMethodAsync('OnPasteRequestedAsync', code)
                        .then(success => {
                            if (!success) {
                                const toast = container.querySelector('.toolbar-copy-toast');
                                if (toast) {
                                    toast.textContent = 'Invalid code';
                                    toast.style.display = '';
                                    setTimeout(() => { toast.style.display = 'none'; }, 2000);
                                }
                            }
                        })
                        .catch(err => console.error('[SVGCanvas] Paste failed.', err));
                }).catch(() => {
                    const toast = container.querySelector('.toolbar-copy-toast');
                    if (toast) {
                        toast.textContent = 'Clipboard unavailable';
                        toast.style.display = '';
                        setTimeout(() => { toast.style.display = 'none'; }, 2000);
                    }
                });
                return;
            }
        }, { signal });
    }

    // Pointer events (unified mouse + touch with sub-pixel precision)
    svg.style.touchAction = 'none'; // prevent browser gestures on touch
    svg.addEventListener('pointerdown', (e) => {
        if (e.button !== 0) return;
        e.preventDefault();
        startStroke(e.clientX, e.clientY);
    }, { signal });
    svg.addEventListener('pointermove', (e) => continueStroke(e.clientX, e.clientY), { signal });
    svg.addEventListener('pointerup', () => endStroke(), { signal });
    svg.addEventListener('pointerleave', () => endStroke(), { signal });
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
 * Sets the Visvalingam-Whyatt minimum area threshold used to simplify strokes
 * when they are completed. A value of 0 disables simplification entirely.
 * Recommended range: 0.5 (subtle) – 8 (aggressive). Default is 2.
 * @param {string} svgId
 * @param {number} minArea
 */
export function setSimplifyMinArea(svgId, minArea) {
    const state = instances.get(svgId);
    if (!state) return;
    state.simplifyMinArea = Math.max(0, minArea);
}

/**
 * Removes the most recently drawn stroke. Returns the remaining stroke count.
 * @param {string} svgId
 * @returns {number}
 */
export function undo(svgId) {
    const state = instances.get(svgId);
    if (!state || state.undoStack.length === 0) return 0;

    const action = state.undoStack.pop();
    if (action.type === 'draw') {
        const idx = state.paths.indexOf(action.element);
        if (idx !== -1) {
            action.index = idx; // Store actual index before removing
            state.paths.splice(idx, 1);
        }
        action.element.remove();
    } else if (action.type === 'erase') {
        state.paths.splice(action.index, 0, action.element);
        // Find correct visual neighbor to maintain Z-order
        let nextEl = null;
        for (let i = action.index + 1; i < state.paths.length; i++) {
            if (state.paths[i].parentNode === state.svg) {
                nextEl = state.paths[i];
                break;
            }
        }
        state.svg.insertBefore(action.element, nextEl);
    } else if (action.type === 'clear') {
        state.paths = [...action.elements];
        for (const el of state.paths) {
            state.svg.appendChild(el);
        }
    }

    state.redoStack.push(action);
    const container = state.svg.closest('.svg-drawing-canvas');
    setUndoDisabled(container, state.undoStack.length === 0);
    setRedoDisabled(container, false);
    return state.paths.length;
}

/**
 * Restores the most recently undone action. Returns the remaining stroke count.
 * @param {string} svgId
 * @returns {number}
 */
export function redo(svgId) {
    const state = instances.get(svgId);
    if (!state || state.redoStack.length === 0) return 0;

    const action = state.redoStack.pop();
    if (action.type === 'draw') {
        const idx = action.index !== undefined ? action.index : state.paths.length;
        state.paths.splice(idx, 0, action.element);
        let nextEl = null;
        for (let i = idx + 1; i < state.paths.length; i++) {
            if (state.paths[i].parentNode === state.svg) {
                nextEl = state.paths[i];
                break;
            }
        }
        state.svg.insertBefore(action.element, nextEl);
    } else if (action.type === 'erase') {
        const idx = state.paths.indexOf(action.element);
        if (idx !== -1) state.paths.splice(idx, 1);
        action.element.remove();
    } else if (action.type === 'clear') {
        for (const path of state.paths) path.remove();
        state.paths = [];
    }

    state.undoStack.push(action);
    const container = state.svg.closest('.svg-drawing-canvas');
    setUndoDisabled(container, false);
    setRedoDisabled(container, state.redoStack.length === 0);
    return state.paths.length;
}

/**
 * Removes all strokes from the canvas.
 * @param {string} svgId
 */
export function clear(svgId) {
    const state = instances.get(svgId);
    if (!state || state.paths.length === 0) return;
    const pathsCopy = [...state.paths];
    for (const path of state.paths) path.remove();
    state.paths = [];
    state.undoStack.push({ type: 'clear', elements: pathsCopy });
    state.redoStack = [];
    const container = state.svg.closest('.svg-drawing-canvas');
    setUndoDisabled(container, false);
    setRedoDisabled(container, true);
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

// Allowlist of SVG element tags and attributes produced by this canvas.
// Used during copy and paste to strip any injected content (scripts, event handlers, etc.)
// that a user could add to the DOM via browser developer tools before sharing.
const ALLOWED_STROKE_TAGS = new Set(['path', 'circle']);
const ALLOWED_STROKE_ATTRS = new Set([
    'd', 'stroke', 'stroke-width', 'fill', 'stroke-linecap', 'stroke-linejoin',
    'cx', 'cy', 'r', 'data-type', 'fill-rule',
]);

/**
 * Creates a sanitized copy of a stroke element (path or circle) containing only
 * the attributes this canvas ever sets, discarding event handlers and foreign content.
 * @param {Element} el - Source SVG element from state.paths or parsed content.
 * @returns {Element|null} A fresh, clean element, or null if the tag is not allowed.
 */
function sanitizeStrokeElement(el) {
    const tag = el.tagName.toLowerCase();
    if (!ALLOWED_STROKE_TAGS.has(tag)) return null;
    const clean = document.createElementNS('http://www.w3.org/2000/svg', tag);
    for (const attr of ALLOWED_STROKE_ATTRS) {
        const val = el.getAttribute(attr);
        if (val !== null) clean.setAttribute(attr, val);
    }
    return clean;
}

/**
 * Rebuilds tracked stroke elements through the sanitization allowlist and returns
 * the resulting SVG inner markup string.
 * @param {Element[]} paths
 * @returns {string}
 */
function serializePaths(paths) {
    if (paths.length === 0) return '';
    const tmp = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    for (const el of paths) {
        const clean = sanitizeStrokeElement(el);
        if (clean) tmp.appendChild(clean);
    }
    return tmp.innerHTML;
}

/**
 * Serializes paths and the current background color into a storable string.
 * Format: `bg:<color>\n<svg markup>` — the bg line is stripped on load.
 * @param {object} state - Canvas instance state.
 * @returns {string}
 */
function serializeWithBackground(state) {
    const markup = serializePaths(state.paths);
    if (!markup) return '';
    const bg = state.svg.style.backgroundColor || state.backgroundColor || 'white';
    return `bg:${bg}\n${markup}`;
}

/**
 * Parses a serialized string that may contain a `bg:` prefix line.
 * @param {string} data
 * @returns {{ background: string|null, markup: string }}
 */
function parseSerializedContent(data) {
    if (data.startsWith('bg:')) {
        const newline = data.indexOf('\n');
        if (newline !== -1) {
            return {
                background: data.substring(3, newline),
                markup: data.substring(newline + 1),
            };
        }
    }
    return { background: null, markup: data };
}

/**
 * Serializes the current drawing as a string used by the server-side copy/paste flow.
 * Only elements tracked in state.paths are included, and each is rebuilt from an
 * attribute allowlist to prevent injected DOM content from being captured.
 * @param {string} svgId
 * @returns {string} Sanitized SVG inner markup, or an empty string if the canvas is empty.
 */
export function getSvgContent(svgId) {
    const state = instances.get(svgId);
    if (!state || state.paths.length === 0) return '';
    return serializePaths(state.paths);
}

/**
 * Serializes the current drawing into a per-instance read cache and returns the total
 * character count of the resulting SVG markup.
 *
 * Complex drawings can produce SVG strings that exceed the default Blazor/SignalR
 * message size limit (32 KB). Calling this function followed by one or more
 * {@link getSvgContentChunk} calls allows the server to retrieve arbitrarily large
 * SVG content without any single SignalR message exceeding the size limit.
 *
 * The cached string is overwritten on each call, so callers must finish reading all
 * chunks before invoking this function again for the same canvas.
 *
 * @param {string} svgId
 * @returns {number} Total character count of the serialized SVG, or 0 if empty.
 */
export function prepareSvgContentForChunkedRead(svgId) {
    const state = instances.get(svgId);
    if (!state || state.paths.length === 0) {
        if (state) state._readCache = '';
        return 0;
    }
    state._readCache = serializePaths(state.paths);
    return state._readCache.length;
}

/**
 * Like prepareSvgContentForChunkedRead but includes background color in the
 * serialized output for copy/paste sharing.
 */
export function prepareSvgContentWithBgForChunkedRead(svgId) {
    const state = instances.get(svgId);
    if (!state || state.paths.length === 0) {
        if (state) state._readCache = '';
        return 0;
    }
    state._readCache = serializeWithBackground(state);
    return state._readCache.length;
}

/**
 * Returns a substring of the SVG content that was cached by the most recent call to
 * {@link prepareSvgContentForChunkedRead} for the same canvas.
 *
 * @param {string} svgId
 * @param {number} start - Zero-based character index to start reading from.
 * @param {number} length - Maximum number of characters to return.
 * @returns {string} The requested substring, or an empty string if no cache is available.
 */
export function getSvgContentChunk(svgId, start, length) {
    const state = instances.get(svgId);
    if (!state || typeof state._readCache !== 'string') return '';
    return state._readCache.substring(start, start + length);
}

/**
 * Clears the current drawing and loads SVG markup produced by {@link getSvgContent}.
 * Parsed elements are re-sanitized through the allowlist before DOM insertion so that
 * any content tampered with after storage cannot execute in the recipient's browser.
 * All loaded strokes are added to the undo stack.
 * @param {string} svgId
 * @param {string} svgContent - SVG inner markup to load.
 * @returns {number} The number of strokes loaded.
 */
export function loadSvgContent(svgId, svgContent) {
    const state = instances.get(svgId);
    if (!state) return 0;

    // Clear existing strokes.
    for (const p of state.paths) p.remove();
    state.paths = [];
    state.undoStack = [];
    state.redoStack = [];
    state.currentPath = null;
    state.currentPoints = [];

    const container = state.svg.closest('.svg-drawing-canvas');

    if (!svgContent) {
        setUndoDisabled(container, true);
        setRedoDisabled(container, true);
        return 0;
    }

    // Parse background color if present.
    const { background, markup } = parseSerializedContent(svgContent);
    if (background) {
        state.svg.style.backgroundColor = background;
    }

    // DOMParser requires a root element; wrap the markup in a temporary <svg>.
    const parser = new DOMParser();
    const doc = parser.parseFromString(
        `<svg xmlns="http://www.w3.org/2000/svg">${markup}</svg>`,
        'image/svg+xml'
    );

    for (const child of [...doc.documentElement.childNodes]) {
        if (child.nodeType !== Node.ELEMENT_NODE) continue;
        // Re-sanitize through the allowlist before inserting into the live DOM.
        const clean = sanitizeStrokeElement(child);
        if (!clean) continue;
        state.svg.appendChild(clean);
        state.paths.push(clean);
        state.undoStack.push({ type: 'draw', element: clean });
    }

    setUndoDisabled(container, state.paths.length === 0);
    setRedoDisabled(container, true);
    return state.paths.length;
}

/**
 * Returns whether a canvas instance is currently registered for the given ID.
 * Used by the .NET host to detect whether the JS-side state was lost (e.g. after
 * a Blazor circuit reconnect) so it can re-initialize before attempting to read
 * or modify the canvas.
 * @param {string} svgId
 * @returns {boolean}
 */
export function isInitialized(svgId) {
    return instances.has(svgId);
}

/**
 * Cleans up instance state for the given canvas.
 * @param {string} svgId
 */
export function dispose(svgId) {
    const state = instances.get(svgId);
    if (state) {
        state.abortController?.abort();
        state.dotNetRef = null;
        state.svg = null;
        state.paths = [];
        state.undoStack = [];
        state.redoStack = [];
    }
    instances.delete(svgId);
}

export const _testExports = { triangleArea, visvalingamWhyatt, buildPath };
