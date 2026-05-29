// Pointer/touch drag capture for the Tracery grid.
//
// Blazor calls init() with the grid container element and a DotNetObjectReference once the
// playing view mounts, and dispose() on teardown. This module only reports *which cell the
// pointer is over*; the Word-Hunt path logic (append / backtrack / submit) lives in
// TraceryGrid.razor.cs so drag and tap share one model. Tap selection is handled entirely on
// the .NET side via @onclick — this module is the drag half.
//
// A press is treated as a *tap* until the pointer crosses into a second cell, at which point it
// is promoted to a *drag*. A pure tap never fires the callbacks below and never preventDefaults,
// so its native click reaches Blazor's @onclick. This is what lets drag and tap coexist on the
// same element without the drag layer eating taps.
//
// .NET callbacks invoked on the supplied ref:
//   OnDragStart(int cellId)  — drag promoted; open a fresh path on the press's start cell.
//   OnDragEnter(int cellId)  — pointer moved into a different cell during a drag.
//   OnDragEnd()              — pointer released after a drag; submit the current path.
//
// Cells must carry a `data-tr-cell="<id>"` attribute. Cells also need `touch-action: none`
// (set in CSS) so a finger drag traces instead of scrolling the page.

// Fraction of each tile (per axis) that counts as a hit target while dragging. Smaller = easier
// diagonals: the outer ring of every tile becomes dead space, so a diagonal drag through the
// corner where four tiles meet no longer clips the two orthogonal neighbours. The visible tile is
// unchanged — only the drag hit-test shrinks. Tap selection (pure Blazor @onclick) and the start
// of a drag still use the full tile, so deliberate presses are never lost. Tune in [0,1].
const DRAG_HIT_INSET = 0.75;

let container = null;
let dotNetRef = null;
let pointerDown = false;     // a pointer is down; still undecided between tap and drag
let dragging = false;        // promoted to a drag (the pointer crossed into another cell)
let startCellId = -1;        // cell the press began on (path start once it promotes to a drag)
let lastCellId = -1;
let activePointerId = null;
let suppressNextClick = false; // swallow the synthetic click a finished drag may emit

// The cell element under (x, y), or null if the point isn't over one of our cells.
function cellElementFromPoint(x, y) {
    const el = document.elementFromPoint(x, y);
    if (!el) return null;
    const cell = el.closest('[data-tr-cell]');
    if (!cell || !container || !container.contains(cell)) return null;
    return cell;
}

function cellIdOf(cell) {
    const id = parseInt(cell.getAttribute('data-tr-cell'), 10);
    return Number.isNaN(id) ? -1 : id;
}

// Full-tile hit-test — used to START a trace, so a deliberate press anywhere on a tile begins there.
function cellIdFromPoint(x, y) {
    const cell = cellElementFromPoint(x, y);
    return cell ? cellIdOf(cell) : -1;
}

// Shrunk hit-test — used WHILE dragging. Returns the cell id only when (x, y) falls inside the
// central DRAG_HIT_INSET box of the tile; the outer ring reads as empty (-1) so diagonals are easy.
function cellIdFromDragPoint(x, y) {
    const cell = cellElementFromPoint(x, y);
    if (!cell) return -1;
    const rect = cell.getBoundingClientRect();
    const halfW = (rect.width * DRAG_HIT_INSET) / 2;
    const halfH = (rect.height * DRAG_HIT_INSET) / 2;
    const cx = rect.left + rect.width / 2;
    const cy = rect.top + rect.height / 2;
    if (Math.abs(x - cx) > halfW || Math.abs(y - cy) > halfH) return -1;
    return cellIdOf(cell);
}

function invoke(method, ...args) {
    if (!dotNetRef) return;
    try {
        dotNetRef.invokeMethodAsync(method, ...args);
    } catch (err) {
        // Ref was disposed between dispatches; ignore.
    }
}

function onPointerDown(ev) {
    // Primary button / single touch only.
    if (ev.button !== undefined && ev.button > 0) return;
    const id = cellIdFromPoint(ev.clientX, ev.clientY);
    if (id < 0) return;
    // Arm the gesture but stay undecided: do NOT preventDefault and do NOT signal .NET yet — a
    // press that never crosses into another cell must remain a pure tap and reach Blazor's
    // @onclick handler. Only a cross into a second cell (in onPointerMove) promotes it to a drag.
    pointerDown = true;
    dragging = false;
    suppressNextClick = false;
    startCellId = id;
    lastCellId = id;
    activePointerId = ev.pointerId;
}

function onPointerMove(ev) {
    if (!pointerDown) return;
    // Use the shrunk hit area mid-drag so the pointer must reach a tile's centre to enter it —
    // the corners shared between tiles are dead space, which is what makes diagonals reliable.
    const id = cellIdFromDragPoint(ev.clientX, ev.clientY);
    if (id < 0 || id === lastCellId) return;

    // First crossing into a different cell promotes this gesture from a possible tap to a drag:
    // open the path on the start cell now (deferred from pointerdown) and capture the pointer.
    if (!dragging) {
        dragging = true;
        try { container.setPointerCapture?.(activePointerId); } catch { /* unsupported */ }
        invoke('OnDragStart', startCellId);
    }
    lastCellId = id;
    ev.preventDefault();
    invoke('OnDragEnter', id);
}

function endDrag(ev) {
    if (!pointerDown) return;
    const wasDragging = dragging;
    pointerDown = false;
    dragging = false;
    lastCellId = -1;
    startCellId = -1;
    if (activePointerId !== null) {
        try { container.releasePointerCapture?.(activePointerId); } catch { /* unsupported */ }
        activePointerId = null;
    }
    // Only a real drag submits through .NET. A tap (no cell crossing) never signalled the drag
    // callbacks, so it falls through to the native click → Blazor @onclick. A finished drag, on
    // the other hand, may emit a trailing click we must swallow so it doesn't re-tap a cell.
    if (wasDragging) {
        suppressNextClick = true;
        invoke('OnDragEnd');
    }
}

// Capture-phase guard: drops the synthetic click that follows a drag before it can reach the
// cell button (and thus Blazor). Taps leave the flag false, so their click passes through.
function onClickCapture(ev) {
    if (!suppressNextClick) return;
    suppressNextClick = false;
    ev.preventDefault();
    ev.stopPropagation();
}

export function init(element, ref) {
    dispose();
    container = element;
    dotNetRef = ref;
    if (!container) return;
    container.addEventListener('pointerdown', onPointerDown);
    container.addEventListener('pointermove', onPointerMove);
    container.addEventListener('pointerup', endDrag);
    container.addEventListener('pointercancel', endDrag);
    container.addEventListener('click', onClickCapture, true); // capture phase
}

export function dispose() {
    if (container) {
        container.removeEventListener('pointerdown', onPointerDown);
        container.removeEventListener('pointermove', onPointerMove);
        container.removeEventListener('pointerup', endDrag);
        container.removeEventListener('pointercancel', endDrag);
        container.removeEventListener('click', onClickCapture, true);
    }
    container = null;
    dotNetRef = null;
    pointerDown = false;
    dragging = false;
    startCellId = -1;
    lastCellId = -1;
    activePointerId = null;
    suppressNextClick = false;
}
