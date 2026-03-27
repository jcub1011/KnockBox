const instances = new Map();

/**
 * Converts a CSS rgb(r, g, b) string to a hex color string.
 * @param {string} rgb - e.g. "rgb(255, 0, 128)"
 * @returns {string|null} hex string e.g. "#ff0080", or null if parsing fails
 */
function rgbToHex(rgb) {
    const match = rgb?.match(/rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)/);
    if (!match) return null;
    return '#' + [match[1], match[2], match[3]]
        .map(n => parseInt(n, 10).toString(16).padStart(2, '0'))
        .join('');
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
 * Applies bidirectional exponential moving average smoothing to reduce hand tremor.
 * Each point is blended with its neighbors using forward and reverse passes, then
 * averaged to avoid directional bias. Endpoints are preserved exactly.
 *
 * @param {{x: number, y: number}[]} points - Input polyline (not mutated).
 * @param {number} [alpha=0.3] - Smoothing factor (0–1). Lower = more smoothing.
 *     1.0 returns the input unchanged. 0.3 is a moderate level that removes
 *     hand tremor while preserving intentional shape features.
 * @returns {{x: number, y: number}[]} A new array with the same length as input.
 */
function emaSmooth(points, alpha = 0.3) {
    const len = points.length;
    if (len <= 2 || alpha >= 1) return points.slice();

    // Forward pass.
    const fwd = [{ x: points[0].x, y: points[0].y }];
    for (let i = 1; i < len; i++) {
        fwd.push({
            x: alpha * points[i].x + (1 - alpha) * fwd[i - 1].x,
            y: alpha * points[i].y + (1 - alpha) * fwd[i - 1].y,
        });
    }

    // Reverse pass.
    const rev = new Array(len);
    rev[len - 1] = { x: points[len - 1].x, y: points[len - 1].y };
    for (let i = len - 2; i >= 0; i--) {
        rev[i] = {
            x: alpha * points[i].x + (1 - alpha) * rev[i + 1].x,
            y: alpha * points[i].y + (1 - alpha) * rev[i + 1].y,
        };
    }

    // Average forward and reverse, preserving endpoints.
    const result = [{ x: points[0].x, y: points[0].y }];
    for (let i = 1; i < len - 1; i++) {
        result.push({
            x: Math.round((fwd[i].x + rev[i].x) * 50) / 100,
            y: Math.round((fwd[i].y + rev[i].y) * 50) / 100,
        });
    }
    result.push({ x: points[len - 1].x, y: points[len - 1].y });
    return result;
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
        /** Visvalingam-Whyatt minimum area threshold for stroke simplification.
         *  0 = no simplification; 0.5 = subtle; 2 = balanced (default); 8 = aggressive. */
        simplifyMinArea: 2,
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
        if (!state.isDrawing || !state.currentPath) return;
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
        if (!state.currentPath) return;

        // Flush any pending rAF so the final stroke segment is not lost.
        if (state._rafPending && state.currentPath) {
            state.currentPath.setAttribute('d', state._pathPrefix + state._pathSuffix);
            state._rafPending = false;
        }

        if (state.currentPoints.length === 1) {
            // Single tap/click — render as a small filled circle.
            const { x, y } = state.currentPoints[0];
            const dot = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
            dot.setAttribute('cx', Math.round(x * 100) / 100);
            dot.setAttribute('cy', Math.round(y * 100) / 100);
            dot.setAttribute('r', Math.round(state.strokeWidth / 2 * 100) / 100);
            dot.setAttribute('fill', state.color);
            svg.replaceChild(dot, state.currentPath);
            state.paths.push(dot);
        } else {
            // Smooth hand tremor, then simplify using Visvalingam-Whyatt.
            const smoothed = emaSmooth(state.currentPoints, 0.7);
            const simplified = visvalingamWhyatt(
                smoothed,
                state.simplifyMinArea ?? 2,
                3
            );
            state.currentPath.setAttribute('d', buildPath(simplified));
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
        });
    }

    // Delegated click handler for swatches, undo, and export.
    if (container) {
        container.addEventListener('click', (e) => {
            // Custom swatch — select it with its current color
            const customSwatchEl = e.target.closest('.toolbar-swatch-custom');
            if (customSwatchEl) {
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
            if (e.target.closest('.toolbar-btn-fill')) {
                const previousBg = svg.style.backgroundColor || state.backgroundColor;
                svg.style.backgroundColor = state.color;
                // Push a sentinel object onto the undo stack so this can be undone.
                state.paths.push({
                    _isFill: true,
                    _previousBg: previousBg,
                    remove() {
                        svg.style.backgroundColor = this._previousBg;
                    }
                });
                setUndoDisabled(container, false);
                state.dotNetRef.invokeMethodAsync('OnStrokeCompleted', state.paths.length)
                    .catch(err => console.error('[SVGCanvas] OnStrokeCompleted failed.', err));
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

// Allowlist of SVG element tags and attributes produced by this canvas.
// Used during copy and paste to strip any injected content (scripts, event handlers, etc.)
// that a user could add to the DOM via browser developer tools before sharing.
const ALLOWED_STROKE_TAGS = new Set(['path', 'circle']);
const ALLOWED_STROKE_ATTRS = new Set([
    'd', 'stroke', 'stroke-width', 'fill', 'stroke-linecap', 'stroke-linejoin',
    'cx', 'cy', 'r',
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
        if (el._isFill) continue; // Skip fill sentinels.
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
    state.currentPath = null;
    state.currentPoints = [];

    const container = state.svg.closest('.svg-drawing-canvas');

    if (!svgContent) {
        setUndoDisabled(container, true);
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
    }

    setUndoDisabled(container, state.paths.length === 0);
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
    instances.delete(svgId);
}

export const _testExports = { triangleArea, emaSmooth, visvalingamWhyatt, buildPath };