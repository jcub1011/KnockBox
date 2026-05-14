/**
 * DnD Mapper rail resize — pointer-driven resize for the left/right side rails.
 *
 * Each instance binds to a single handle element. On pointerdown the module
 * captures the pointer and reports width changes back to the .NET component
 * via OnRailResize(side, pixels). The .NET side applies clamping (min/max)
 * and writes the value as an inline CSS variable on the playing-phase root.
 */

const instances = new Map();

const MIN_PX = 200;
const MAX_PX = 600;

function clamp(px) {
    return Math.max(MIN_PX, Math.min(MAX_PX, px));
}

/**
 * @param {HTMLElement} handle
 * @param {string} side          'left' | 'right'
 * @param {object}    dotNetRef  .NET reference exposing OnRailResize(side, px)
 */
export function attach(handle, side, dotNetRef) {
    if (!handle) return;
    const key = handle;
    detach(handle);

    const state = { side, dotNetRef, pointerId: null, startX: 0, startWidth: 0 };

    function onPointerDown(e) {
        if (e.button !== 0) return;
        state.pointerId = e.pointerId;
        state.startX = e.clientX;
        // Read the *current* width by walking up to the rail aside element.
        const rail = handle.closest('aside');
        state.startWidth = rail ? rail.getBoundingClientRect().width : 0;
        try { handle.setPointerCapture(e.pointerId); } catch { /* ignore */ }
        document.body.style.cursor = 'col-resize';
        document.body.style.userSelect = 'none';
        e.preventDefault();
        e.stopPropagation();
    }

    function onPointerMove(e) {
        if (state.pointerId !== e.pointerId) return;
        const dx = e.clientX - state.startX;
        // Left rail grows as the handle moves right; right rail shrinks.
        const delta = side === 'left' ? dx : -dx;
        const px = clamp(state.startWidth + delta);
        if (state.dotNetRef) {
            state.dotNetRef.invokeMethodAsync('OnRailResize', side, px)
                .catch(err => console.error('[DndMapperRailResize] OnRailResize failed.', err));
        }
    }

    function onPointerUp(e) {
        if (state.pointerId !== e.pointerId) return;
        state.pointerId = null;
        try { handle.releasePointerCapture(e.pointerId); } catch { /* ignore */ }
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
    }

    handle.addEventListener('pointerdown', onPointerDown);
    handle.addEventListener('pointermove', onPointerMove);
    handle.addEventListener('pointerup', onPointerUp);
    handle.addEventListener('pointercancel', onPointerUp);

    instances.set(key, { handle, onPointerDown, onPointerMove, onPointerUp, state });
}

export function detach(handle) {
    if (!handle) return;
    const rec = instances.get(handle);
    if (!rec) return;
    rec.handle.removeEventListener('pointerdown', rec.onPointerDown);
    rec.handle.removeEventListener('pointermove', rec.onPointerMove);
    rec.handle.removeEventListener('pointerup', rec.onPointerUp);
    rec.handle.removeEventListener('pointercancel', rec.onPointerUp);
    rec.state.dotNetRef = null;
    instances.delete(handle);
}

const STORAGE_PREFIX = 'dndm.rail.';

export function load(side, role, fallback) {
    try {
        const v = window.localStorage.getItem(STORAGE_PREFIX + role + '.' + side);
        if (!v) return fallback;
        const n = parseInt(v, 10);
        return Number.isFinite(n) ? clamp(n) : fallback;
    } catch { return fallback; }
}

export function save(side, role, px) {
    try {
        window.localStorage.setItem(STORAGE_PREFIX + role + '.' + side, String(Math.round(px)));
    } catch { /* ignore quota / privacy errors */ }
}
