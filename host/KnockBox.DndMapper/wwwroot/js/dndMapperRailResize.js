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
 * @param {object}    dotNetRef  .NET reference exposing OnRailResize(side, px, persist)
 * @param {HTMLElement} rootEl   the playing-phase root that owns the CSS vars
 */
export function attach(handle, side, dotNetRef, rootEl) {
    if (!handle) return;
    const key = handle;
    detach(handle);

    // The drag loop intentionally bypasses Blazor: writing the CSS variable on
    // the root element is ~free and lets the rail track the cursor at native
    // frame rate. Going through .NET on every pointermove triggers a full
    // component-tree re-render of the playing phase per move, which is what
    // caused the lag. We only round-trip to .NET on pointerup to sync state
    // and persist the chosen width.
    const cssVar = side === 'left' ? '--dndm-rail-w-left' : '--dndm-rail-w-right';

    // Movement (in pixels) below which a pointerup is treated as a click — the
    // rail toggles collapsed instead of resizing. 4px tolerates jitter on
    // small movements / touchpads without swallowing intentional drags.
    const CLICK_THRESHOLD_PX = 4;

    const state = {
        side, dotNetRef, rootEl,
        pointerId: null,
        startX: 0,
        startWidth: 0,
        currentPx: null,
        moved: false,
    };

    function onPointerDown(e) {
        if (e.button !== 0) return;
        state.pointerId = e.pointerId;
        state.startX = e.clientX;
        state.moved = false;
        // Read the *current* width by walking up to the rail aside element.
        const rail = handle.closest('aside');
        state.startWidth = rail ? rail.getBoundingClientRect().width : 0;
        state.currentPx = state.startWidth;
        try { handle.setPointerCapture(e.pointerId); } catch { /* ignore */ }
        document.body.style.cursor = 'col-resize';
        document.body.style.userSelect = 'none';
        e.preventDefault();
        e.stopPropagation();
    }

    function onPointerMove(e) {
        if (state.pointerId !== e.pointerId) return;
        const dx = e.clientX - state.startX;
        if (!state.moved && Math.abs(dx) < CLICK_THRESHOLD_PX) {
            // Within the click jitter zone — defer any visual change until the
            // user actually drags past the threshold, so a click that misses
            // by a pixel still gets interpreted as a collapse-toggle.
            return;
        }
        state.moved = true;
        // Left rail grows as the handle moves right; right rail shrinks.
        const delta = side === 'left' ? dx : -dx;
        const px = clamp(state.startWidth + delta);
        state.currentPx = px;
        // Direct DOM write — no Blazor, no SignalR round-trip. The CSS variable
        // change reflows the grid track immediately.
        if (state.rootEl) state.rootEl.style.setProperty(cssVar, px + 'px');
    }

    function onPointerUp(e) {
        if (state.pointerId !== e.pointerId) return;
        state.pointerId = null;
        try { handle.releasePointerCapture(e.pointerId); } catch { /* ignore */ }
        document.body.style.cursor = '';
        document.body.style.userSelect = '';

        if (!state.dotNetRef) return;

        if (state.moved) {
            // Drag → resize. Sync .NET state + persist with a single interop
            // call. The subsequent Blazor render writes the same CSS var via
            // the inline style, matching what we already applied, so the rail
            // doesn't flicker.
            const finalPx = state.currentPx;
            if (finalPx !== null) {
                state.dotNetRef.invokeMethodAsync('OnRailResize', side, finalPx, /*persist*/ true)
                    .catch(err => console.error('[DndMapperRailResize] OnRailResize failed.', err));
            }
        } else {
            // Click without drag → toggle the rail's collapsed state.
            state.dotNetRef.invokeMethodAsync('OnRailToggleCollapse', side)
                .catch(err => console.error('[DndMapperRailResize] OnRailToggleCollapse failed.', err));
        }
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
