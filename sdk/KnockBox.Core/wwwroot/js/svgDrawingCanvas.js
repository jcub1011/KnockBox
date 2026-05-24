const instances = new Map();

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

function triangleArea(a, b, c) {
    return Math.abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y));
}

/**
 * Simplifies an array of {x, y} points using the Visvalingam-Whyatt algorithm.
 * Iteratively removes the point whose removal causes the least shape change,
 * preserving high-curvature features while pruning near-collinear runs.
 */
function visvalingamWhyatt(points, minArea = 2, minPoints = 3) {
    const len = points.length;
    if (len <= minPoints) return points.slice();

    const nodes = points.map((p, i) => ({ x: p.x, y: p.y, index: i, prev: null, next: null, area: Infinity }));
    for (let i = 0; i < len; i++) {
        nodes[i].prev = nodes[i - 1] || null;
        nodes[i].next = nodes[i + 1] || null;
    }
    for (let i = 1; i < len - 1; i++) {
        nodes[i].area = triangleArea(nodes[i].prev, nodes[i], nodes[i].next);
    }

    let remaining = len;
    while (remaining > minPoints) {
        let minNode = null;
        let minVal = Infinity;
        let cur = nodes[0].next;
        while (cur && cur.next) {
            if (cur.area < minVal) {
                minVal = cur.area;
                minNode = cur;
            }
            cur = cur.next;
        }
        if (!minNode || minVal >= minArea) break;

        minNode.prev.next = minNode.next;
        minNode.next.prev = minNode.prev;
        remaining--;
        const prev = minNode.prev;
        const next = minNode.next;
        if (prev.prev) prev.area = Math.max(triangleArea(prev.prev, prev, next), minVal);
        if (next.next) next.area = Math.max(triangleArea(prev, next, next.next), minVal);
    }

    const result = [];
    let node = nodes[0];
    while (node) {
        result.push({ x: node.x, y: node.y });
        node = node.next;
    }
    return result;
}

function getPixelColor(data, x, y, width) {
    const idx = (y * width + x) * 4;
    return (data[idx] << 24) | (data[idx + 1] << 16) | (data[idx + 2] << 8) | data[idx + 3];
}

/**
 * Span-based flood fill on ImageData, returning a bitmask of the filled area.
 */
function floodFillSpan(imageData, x, y, fillColor) {
    const data = imageData.data;
    const width = imageData.width;
    const height = imageData.height;
    const targetColor = getPixelColor(data, x, y, width);

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
        while (lx > 0 && getPixelColor(data, lx - 1, ly, width) === targetColor && !mask[ly * width + (lx - 1)]) lx--;
        while (rx < width - 1 && getPixelColor(data, rx + 1, ly, width) === targetColor && !mask[ly * width + (rx + 1)]) rx++;

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
 * Builds a smooth quadratic Bézier path string from an array of {x, y} points.
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
 */
function triggerSvgDownload(state, fileName, backgroundColor) {
    const svgEl = state.svg;
    const vb = svgEl.viewBox?.baseVal;
    const hasViewBox = vb && vb.width > 0 && vb.height > 0;
    const width = hasViewBox ? vb.width : Math.round(svgEl.getBoundingClientRect().width);
    const height = hasViewBox ? vb.height : Math.round(svgEl.getBoundingClientRect().height);

    const clone = svgEl.cloneNode(true);
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
 * Notifies the .NET side of the current stroke count plus undo/redo availability.
 */
function notifyStrokeCompleted(state) {
    const canUndo = state.undoStack.length > 0;
    const canRedo = state.redoStack.length > 0;
    state.dotNetRef?.invokeMethodAsync('OnStrokeCompleted', state.paths.length, canUndo, canRedo)
        .catch(err => console.error('[SVGCanvas] OnStrokeCompleted failed.', err));
}

/**
 * Initializes the SVG drawing engine for a given <svg> element id.
 * Pointer/touch handlers are attached to the SVG itself. The toolbar (if any) is a
 * separate Blazor component that drives the engine via the named verb exports below.
 */
export function initialize(svgId, dotNetRef, initialColor, initialStrokeWidth, initialBackgroundColor) {
    const svg = document.getElementById(svgId);
    if (!svg) {
        console.error(`[SVGCanvas] initialize: element "${svgId}" not found in the DOM.`);
        return;
    }

    // If re-initializing (e.g. after a circuit reconnect), tear down the prior listeners.
    const prior = instances.get(svgId);
    if (prior) {
        prior.abortController?.abort();
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
        /** Visvalingam-Whyatt simplification threshold (0 disables; 2 default). */
        simplifyMinArea: 2,
        currentTool: 'brush',
        undoStack: [],
        redoStack: [],
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

    function eraseAt(clientX, clientY) {
        const el = document.elementFromPoint(clientX, clientY);
        if (el && (el.tagName === 'path' || el.tagName === 'circle') && el.parentNode === svg) {
            const idx = state.paths.indexOf(el);
            if (idx !== -1) {
                state.paths.splice(idx, 1);
                el.remove();
                state.undoStack.push({ type: 'erase', element: el, index: idx });
                state.redoStack = [];
                notifyStrokeCompleted(state);
            }
        }
    }

    function performFloodFillAt(clientX, clientY) {
        const { x, y } = getSvgCoords(clientX, clientY);
        const SCALE = 2;
        const vb = svg.viewBox?.baseVal;
        const viewWidth = (vb && vb.width > 0) ? vb.width : svg.clientWidth;
        const viewHeight = (vb && vb.height > 0) ? vb.height : svg.clientHeight;
        const width = Math.round(viewWidth * SCALE);
        const height = Math.round(viewHeight * SCALE);

        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const ctx = canvas.getContext('2d', { willReadFrequently: true });
        ctx.scale(SCALE, SCALE);

        ctx.fillStyle = svg.style.backgroundColor || state.backgroundColor;
        ctx.fillRect(0, 0, viewWidth, viewHeight);

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
                // Fills should NOT be stroked during rasterization to prevent bleed
                // into adjacent areas (which would block subsequent fills).
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
        if (colorsMatch(targetColor, fillIntValue, 2)) return;

        const mask = floodFillSpan(imageData, ix, iy, state.color);
        if (!mask) return;
        const d = maskToPath(mask, width, height, SCALE);
        if (!d) return;

        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('data-type', 'fill');
        path.setAttribute('fill', state.color);
        path.setAttribute('stroke', state.color);
        path.setAttribute('stroke-width', '1'); // Reduce bleed.
        path.setAttribute('stroke-linejoin', 'round');
        path.setAttribute('d', d);

        // Insertion: keep fills behind strokes, newer fills on top of older fills.
        let lastFillIdx = -1;
        for (let i = 0; i < state.paths.length; i++) {
            if (state.paths[i].getAttribute('data-type') === 'fill') lastFillIdx = i;
            else break;
        }
        const insertionIdx = lastFillIdx + 1;
        const nextElInDom = state.paths[insertionIdx];
        svg.insertBefore(path, nextElInDom || null);
        state.paths.splice(insertionIdx, 0, path);
        state.undoStack.push({ type: 'draw', element: path });
        state.redoStack = [];
        notifyStrokeCompleted(state);
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
        if (state.isErasing) { eraseAt(clientX, clientY); return; }
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

        if (state._rafPending && state.currentPath) {
            state.currentPath.setAttribute('d', state._pathPrefix + state._pathSuffix);
            state._rafPending = false;
        }

        let element;
        if (state.currentPoints.length === 1) {
            const { x, y } = state.currentPoints[0];
            const dot = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
            dot.setAttribute('cx', Math.round(x * 100) / 100);
            dot.setAttribute('cy', Math.round(y * 100) / 100);
            dot.setAttribute('r', Math.round(state.strokeWidth / 2 * 100) / 100);
            dot.setAttribute('fill', state.color);
            svg.replaceChild(dot, state.currentPath);
            element = dot;
        } else {
            const simplified = visvalingamWhyatt(state.currentPoints, state.simplifyMinArea ?? 2, 3);
            state.currentPath.setAttribute('d', buildPath(simplified));
            element = state.currentPath;
        }

        state.paths.push(element);
        state.undoStack.push({ type: 'draw', element });
        state.redoStack = [];
        state.currentPath = null;
        state.currentPoints = [];
        notifyStrokeCompleted(state);
    }

    // Pointer events (unified mouse + touch with sub-pixel precision).
    svg.style.touchAction = 'none';
    svg.addEventListener('pointerdown', (e) => {
        if (e.button !== 0) return;
        e.preventDefault();
        startStroke(e.clientX, e.clientY);
    }, { signal });
    svg.addEventListener('pointermove', (e) => continueStroke(e.clientX, e.clientY), { signal });
    svg.addEventListener('pointerup', () => endStroke(), { signal });
    svg.addEventListener('pointerleave', () => endStroke(), { signal });
}

/** Updates the current stroke color. */
export function setColor(svgId, color) {
    const state = instances.get(svgId);
    if (!state) return;
    state.color = color;
}

/** Updates the current stroke width. */
export function setStrokeWidth(svgId, width) {
    const state = instances.get(svgId);
    if (!state) return;
    state.strokeWidth = width;
}

/** Sets the active tool ('brush' | 'eraser' | 'fill'). */
export function setTool(svgId, tool) {
    const state = instances.get(svgId);
    if (!state) return;
    state.currentTool = (tool === 'eraser' || tool === 'fill') ? tool : 'brush';
}

/**
 * Sets the Visvalingam-Whyatt minimum area threshold used to simplify strokes.
 * 0 disables simplification. Recommended range: 0.5–8. Default 2.
 */
export function setSimplifyMinArea(svgId, minArea) {
    const state = instances.get(svgId);
    if (!state) return;
    state.simplifyMinArea = Math.max(0, minArea);
}

/** Removes the most recent action. */
export function undo(svgId) {
    const state = instances.get(svgId);
    if (!state || state.undoStack.length === 0) return;

    const action = state.undoStack.pop();
    if (action.type === 'draw') {
        const idx = state.paths.indexOf(action.element);
        if (idx !== -1) {
            action.index = idx;
            state.paths.splice(idx, 1);
        }
        action.element.remove();
    } else if (action.type === 'erase') {
        state.paths.splice(action.index, 0, action.element);
        let nextEl = null;
        for (let i = action.index + 1; i < state.paths.length; i++) {
            if (state.paths[i].parentNode === state.svg) { nextEl = state.paths[i]; break; }
        }
        state.svg.insertBefore(action.element, nextEl);
    } else if (action.type === 'clear') {
        state.paths = [...action.elements];
        for (const el of state.paths) state.svg.appendChild(el);
    }
    state.redoStack.push(action);
    notifyStrokeCompleted(state);
}

/** Restores the most recently undone action. */
export function redo(svgId) {
    const state = instances.get(svgId);
    if (!state || state.redoStack.length === 0) return;

    const action = state.redoStack.pop();
    if (action.type === 'draw') {
        const idx = action.index !== undefined ? action.index : state.paths.length;
        state.paths.splice(idx, 0, action.element);
        let nextEl = null;
        for (let i = idx + 1; i < state.paths.length; i++) {
            if (state.paths[i].parentNode === state.svg) { nextEl = state.paths[i]; break; }
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
    notifyStrokeCompleted(state);
}

/** Removes all strokes from the canvas. */
export function clear(svgId) {
    const state = instances.get(svgId);
    if (!state) return;
    if (state.paths.length === 0) {
        // Still notify so the toolbar refreshes after a no-op clear of empty canvas.
        notifyStrokeCompleted(state);
        return;
    }
    const pathsCopy = [...state.paths];
    for (const path of state.paths) path.remove();
    state.paths = [];
    state.undoStack.push({ type: 'clear', elements: pathsCopy });
    state.redoStack = [];
    notifyStrokeCompleted(state);
}

/** Triggers a browser file download of the current drawing as an SVG. */
export function downloadSvg(svgId, fileName, backgroundColor) {
    const state = instances.get(svgId);
    if (!state) return;
    triggerSvgDownload(state, fileName, backgroundColor);
}

// Allowlist of SVG element tags and attributes produced by this engine.
const ALLOWED_STROKE_TAGS = new Set(['path', 'circle']);
const ALLOWED_STROKE_ATTRS = new Set([
    'd', 'stroke', 'stroke-width', 'fill', 'stroke-linecap', 'stroke-linejoin',
    'cx', 'cy', 'r', 'data-type', 'fill-rule',
]);

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

function serializePaths(paths) {
    if (paths.length === 0) return '';
    const tmp = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    for (const el of paths) {
        const clean = sanitizeStrokeElement(el);
        if (clean) tmp.appendChild(clean);
    }
    return tmp.innerHTML;
}

/** Serializes paths plus background color into a storable string. */
function serializeWithBackground(state) {
    const markup = serializePaths(state.paths);
    if (!markup) return '';
    const bg = state.svg.style.backgroundColor || state.backgroundColor || 'white';
    return `bg:${bg}\n${markup}`;
}

function parseSerializedContent(data) {
    if (data.startsWith('bg:')) {
        const newline = data.indexOf('\n');
        if (newline !== -1) {
            return { background: data.substring(3, newline), markup: data.substring(newline + 1) };
        }
    }
    return { background: null, markup: data };
}

/** Returns sanitized SVG inner markup for the current drawing, or '' if empty. */
export function getSvgContent(svgId) {
    const state = instances.get(svgId);
    if (!state || state.paths.length === 0) return '';
    return serializePaths(state.paths);
}

/**
 * Serializes the drawing into a per-instance read cache and returns the total length.
 * Pair with {@link getSvgContentChunk} to retrieve large drawings without exceeding
 * the default 32 KB SignalR message size.
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

/** Like prepareSvgContentForChunkedRead but includes background color for sharing. */
export function prepareSvgContentWithBgForChunkedRead(svgId) {
    const state = instances.get(svgId);
    if (!state || state.paths.length === 0) {
        if (state) state._readCache = '';
        return 0;
    }
    state._readCache = serializeWithBackground(state);
    return state._readCache.length;
}

export function getSvgContentChunk(svgId, start, length) {
    const state = instances.get(svgId);
    if (!state || typeof state._readCache !== 'string') return '';
    return state._readCache.substring(start, start + length);
}

/** Clears the current drawing and loads sanitized SVG markup. Returns stroke count. */
export function loadSvgContent(svgId, svgContent) {
    const state = instances.get(svgId);
    if (!state) return 0;

    for (const p of state.paths) p.remove();
    state.paths = [];
    state.undoStack = [];
    state.redoStack = [];
    state.currentPath = null;
    state.currentPoints = [];

    if (!svgContent) {
        notifyStrokeCompleted(state);
        return 0;
    }

    const { background, markup } = parseSerializedContent(svgContent);
    if (background) state.svg.style.backgroundColor = background;

    const parser = new DOMParser();
    const doc = parser.parseFromString(
        `<svg xmlns="http://www.w3.org/2000/svg">${markup}</svg>`,
        'image/svg+xml');

    for (const child of [...doc.documentElement.childNodes]) {
        if (child.nodeType !== Node.ELEMENT_NODE) continue;
        const clean = sanitizeStrokeElement(child);
        if (!clean) continue;
        state.svg.appendChild(clean);
        state.paths.push(clean);
        state.undoStack.push({ type: 'draw', element: clean });
    }

    notifyStrokeCompleted(state);
    return state.paths.length;
}

export function isInitialized(svgId) {
    return instances.has(svgId);
}

/** Cleans up instance state for the given canvas. */
export function dispose(svgId) {
    const state = instances.get(svgId);
    if (state) {
        state.abortController?.abort();
        state.dotNetRef = null;
        if (state.svg) state.svg.innerHTML = '';
        state.svg = null;
        state.paths = [];
        state.undoStack = [];
        state.redoStack = [];
    }
    instances.delete(svgId);
}

// ── Clipboard helpers used by the default toolbar ──────────────────────────────

export function writeClipboardText(text) {
    if (!navigator.clipboard) return Promise.reject(new Error('clipboard unavailable'));
    return navigator.clipboard.writeText(text);
}

export function readClipboardText() {
    if (!navigator.clipboard) return Promise.reject(new Error('clipboard unavailable'));
    return navigator.clipboard.readText();
}

export const _testExports = { triangleArea, visvalingamWhyatt, buildPath };
