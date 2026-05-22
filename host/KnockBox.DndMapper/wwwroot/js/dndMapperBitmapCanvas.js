/**
 * DnD Mapper bitmap canvas — Canvas Viewport Method renderer.
 *
 * Replaces the prior bitmap layer (.dndm-image-layer with one <img> per map
 * image, all riding a single CSS scale on .dndm-canvas-transform). That model
 * tipped over at large zoom: a transformed <img> layer whose bounding box
 * dwarfs the viewport forces the compositor to manage huge backing stores
 * and, past the GPU's max texture size, falls back to software rasterization.
 *
 * Here a single viewport-sized <canvas> sits as a sibling of (not inside)
 * .dndm-canvas-transform. On every pan/zoom step the canvas redraws each
 * visible image at its current world rect transformed into stage CSS px. The
 * compositor only ever sees a viewport-sized layer; per-frame draw cost is
 * proportional to viewport size, not source size.
 *
 * Public surface:
 *   initialize(canvasId, dotNetRef, widthCells, heightCells, cellPx)
 *   setImages(canvasId, images)
 *     // images: [{ id, src, x, y, width, height, rotation, opacity,
 *                   layerOrder, hidden }]
 *   setViewport(canvasId, panX, panY, zoom, cellPx)
 *     // Marks dirty; the next RAF redraws.
 *   setInFlightTransform(canvasId, imageId, { x, y, width, height, rotation })
 *     // Used by dndMapperImageDrag during in-flight drag/resize/rotate so
 *     // the canvas reflects the gesture before it commits to .NET.
 *   clearInFlightTransform(canvasId, imageId)
 *   dispose(canvasId)
 *
 * The world→stage transform mirrors dndMapperViewport.js's pan/zoom math:
 *   stage_px(world_cells) = (world_cells - pan) * cellPx * zoom
 * which the wheel-anchor handler already relies on (transform-origin: 50% 50%
 * on the wrapper makes the W*(zoom-1)/2 term in the wrapper transform cancel
 * out cleanly). Keep this formula in sync with the viewport module — they're
 * two ends of one coordinate convention.
 */

const instances = new Map();

export function initialize(canvasId, dotNetRef, widthCells, heightCells, cellPx) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) {
        console.error(`[DndMapperBitmapCanvas] initialize: element "${canvasId}" not found.`);
        return;
    }
    dispose(canvasId);

    const stage = canvas.closest('.dndm-canvas-stage') || canvas.parentElement;
    const ctx = canvas.getContext('2d', { alpha: true });

    const state = {
        canvas,
        ctx,
        stage,
        dotNetRef,
        widthCells: widthCells || 0,
        heightCells: heightCells || 0,
        cellPx: cellPx > 0 ? cellPx : 1,
        panX: 0,
        panY: 0,
        zoom: 1,
        images: [],
        bitmaps: new Map(),       // id -> ImageBitmap | 'loading' | 'failed'
        inFlight: new Map(),      // id -> partial transform overrides
        dirty: true,
        rafHandle: 0,
        resizeObserver: null,
        disposed: false,
    };
    instances.set(canvasId, state);

    state.resizeObserver = new ResizeObserver(() => markDirty(state));
    if (stage) state.resizeObserver.observe(stage);

    scheduleRedraw(state);
}

export function setImages(canvasId, images) {
    const state = instances.get(canvasId);
    if (!state) return;
    state.images = images || [];

    // Evict bitmaps for images that are no longer present.
    const liveIds = new Set();
    for (const img of state.images) liveIds.add(img.id);
    for (const [id, bm] of state.bitmaps) {
        if (!liveIds.has(id)) {
            if (bm && bm !== 'loading' && bm !== 'failed') {
                try { bm.close?.(); } catch { /* ignore */ }
            }
            state.bitmaps.delete(id);
        }
    }

    // Kick off bitmap decode for any image whose src changed or is new.
    for (const img of state.images) {
        if (!img.src) {
            // No src => placeholder; ensure no stale bitmap stays cached under this id.
            const prev = state.bitmaps.get(img.id);
            if (prev && prev !== 'loading' && prev !== 'failed') {
                try { prev.close?.(); } catch { /* ignore */ }
                state.bitmaps.set(img.id, 'failed');
            } else if (!prev) {
                state.bitmaps.set(img.id, 'failed');
            }
            continue;
        }
        const existing = state.bitmaps.get(img.id);
        const existingSrc = state.bitmaps.get(img.id + '#src');
        if (existing && existingSrc === img.src) continue;
        if (existing && existing !== 'loading' && existing !== 'failed') {
            try { existing.close?.(); } catch { /* ignore */ }
        }
        state.bitmaps.set(img.id + '#src', img.src);
        loadBitmap(state, img.id, img.src);
    }
    markDirty(state);
}

export function setViewport(canvasId, panX, panY, zoom, cellPx) {
    const state = instances.get(canvasId);
    if (!state) return;
    state.panX = panX;
    state.panY = panY;
    state.zoom = zoom > 0 ? zoom : 1;
    if (cellPx > 0) state.cellPx = cellPx;
    markDirty(state);
}

export function setBounds(canvasId, widthCells, heightCells, cellPx) {
    const state = instances.get(canvasId);
    if (!state) return;
    state.widthCells = widthCells;
    state.heightCells = heightCells;
    if (cellPx > 0) state.cellPx = cellPx;
    markDirty(state);
}

export function setInFlightTransform(canvasId, imageId, transform) {
    const state = instances.get(canvasId);
    if (!state) return;
    state.inFlight.set(imageId, transform);
    markDirty(state);
}

export function clearInFlightTransform(canvasId, imageId) {
    const state = instances.get(canvasId);
    if (!state) return;
    if (!state.inFlight.has(imageId)) return;
    state.inFlight.delete(imageId);
    markDirty(state);
}

export function dispose(canvasId) {
    const state = instances.get(canvasId);
    if (!state) return;
    state.disposed = true;
    try { state.resizeObserver?.disconnect(); } catch { /* ignore */ }
    if (state.rafHandle) {
        try { cancelAnimationFrame(state.rafHandle); } catch { /* ignore */ }
    }
    for (const [, bm] of state.bitmaps) {
        if (bm && bm !== 'loading' && bm !== 'failed') {
            try { bm.close?.(); } catch { /* ignore */ }
        }
    }
    state.bitmaps.clear();
    state.inFlight.clear();
    state.dotNetRef = null;
    instances.delete(canvasId);
}

function markDirty(state) {
    state.dirty = true;
    scheduleRedraw(state);
}

function scheduleRedraw(state) {
    if (state.rafHandle || state.disposed) return;
    state.rafHandle = requestAnimationFrame(() => {
        state.rafHandle = 0;
        if (state.disposed || !state.dirty) return;
        state.dirty = false;
        try { redraw(state); }
        catch (err) { console.error('[DndMapperBitmapCanvas] redraw failed.', err); }
    });
}

async function loadBitmap(state, id, src) {
    state.bitmaps.set(id, 'loading');
    try {
        const response = await fetch(src);
        if (!response.ok) throw new Error(`fetch ${response.status}`);
        const blob = await response.blob();
        const bm = await createImageBitmap(blob);
        if (state.disposed || state.bitmaps.get(id + '#src') !== src) {
            // Either disposed or the image's src changed while we were
            // decoding — drop this bitmap on the floor.
            try { bm.close?.(); } catch { /* ignore */ }
            return;
        }
        state.bitmaps.set(id, bm);
        markDirty(state);
    } catch (err) {
        if (state.disposed) return;
        state.bitmaps.set(id, 'failed');
        markDirty(state);
    }
}

function redraw(state) {
    const canvas = state.canvas;
    const stage = state.stage;
    if (!canvas) return;

    const stageRect = stage ? stage.getBoundingClientRect()
                            : { width: canvas.clientWidth, height: canvas.clientHeight };
    const cssW = Math.max(1, stageRect.width);
    const cssH = Math.max(1, stageRect.height);
    const dpr = window.devicePixelRatio || 1;
    const backW = Math.max(1, Math.round(cssW * dpr));
    const backH = Math.max(1, Math.round(cssH * dpr));
    if (canvas.width !== backW) canvas.width = backW;
    if (canvas.height !== backH) canvas.height = backH;
    const cssWStyle = `${cssW}px`;
    const cssHStyle = `${cssH}px`;
    if (canvas.style.width !== cssWStyle) canvas.style.width = cssWStyle;
    if (canvas.style.height !== cssHStyle) canvas.style.height = cssHStyle;

    const ctx = state.ctx;
    // CSS-pixel coordinate space — drawImage's destination dims read as CSS px.
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, cssW, cssH);
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = 'high';

    const pxPerCell = state.cellPx * state.zoom;
    if (pxPerCell <= 0) return;
    // Stage CSS px of world (0,0). The wrapper's transform-origin:50% 50%
    // makes the W*(zoom-1)/2 term in the wrapper transform cancel out, so
    // this reduces to: world_origin_stage_px = -pan * cellPx * zoom.
    const worldOriginX = -state.panX * pxPerCell;
    const worldOriginY = -state.panY * pxPerCell;

    // Lower layer order paints first so higher layers sit on top — mirrors
    // the SVG paint order on the picker rects.
    const sorted = state.images.slice().sort((a, b) => a.layerOrder - b.layerOrder);

    for (const img of sorted) {
        if (img.hidden) continue;

        const override = state.inFlight.get(img.id);
        const ix = override?.x ?? img.x;
        const iy = override?.y ?? img.y;
        const iw = override?.width ?? img.width;
        const ih = override?.height ?? img.height;
        const irot = override?.rotation ?? img.rotation ?? 0;
        const iopacity = img.opacity ?? 1;

        const screenX = worldOriginX + ix * pxPerCell;
        const screenY = worldOriginY + iy * pxPerCell;
        const screenW = iw * pxPerCell;
        const screenH = ih * pxPerCell;
        if (screenW <= 0 || screenH <= 0) continue;

        // Cheap conservative cull — circumscribed circle vs canvas rect.
        // Rotation is uniform so the circumscribed circle covers any angle.
        const cx = screenX + screenW / 2;
        const cy = screenY + screenH / 2;
        const radius = 0.5 * Math.hypot(screenW, screenH);
        if (cx + radius < 0 || cy + radius < 0 || cx - radius > cssW || cy - radius > cssH) continue;

        const bm = state.bitmaps.get(img.id);
        const haveBitmap = bm && bm !== 'loading' && bm !== 'failed';

        ctx.save();
        ctx.globalAlpha = Math.max(0, Math.min(1, iopacity));
        ctx.translate(cx, cy);
        if (irot !== 0) ctx.rotate(irot * Math.PI / 180);
        if (haveBitmap) {
            ctx.drawImage(bm, -screenW / 2, -screenH / 2, screenW, screenH);
        } else {
            // Placeholder: matches the previous .dndm-image-placeholder visuals
            // (dim translucent rect with a thin dashed-ish border feel). The
            // opacity is further halved so the user sees "in-flight load" vs
            // "fully resolved".
            ctx.globalAlpha = Math.max(0, Math.min(0.6, iopacity));
            ctx.fillStyle = 'rgba(80, 75, 68, 0.5)';
            ctx.fillRect(-screenW / 2, -screenH / 2, screenW, screenH);
            ctx.lineWidth = 1;
            ctx.strokeStyle = 'rgba(196, 116, 56, 0.6)';
            ctx.setLineDash([6, 6]);
            ctx.strokeRect(-screenW / 2, -screenH / 2, screenW, screenH);
            ctx.setLineDash([]);
        }
        ctx.restore();
    }
}
