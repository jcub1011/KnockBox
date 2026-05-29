// Pointer/touch drag capture for the Tracery grid.
//
// Blazor calls init() with the grid container element and a DotNetObjectReference once the
// playing view mounts, and dispose() on teardown. This module only reports *which cell the
// pointer is over*; the Word-Hunt path logic (append / backtrack / submit) lives in
// TraceryGrid.razor.cs so drag and tap share one model. Tap selection is handled entirely on
// the .NET side via @onclick — this module is the drag half.
//
// .NET callbacks invoked on the supplied ref:
//   OnDragStart(int cellId)  — pointer went down on a cell; start a fresh path there.
//   OnDragEnter(int cellId)  — pointer moved into a different cell during a drag.
//   OnDragEnd()              — pointer released; submit the current path.
//
// Cells must carry a `data-tr-cell="<id>"` attribute. Cells also need `touch-action: none`
// (set in CSS) so a finger drag traces instead of scrolling the page.

let container = null;
let dotNetRef = null;
let dragging = false;
let lastCellId = -1;

function cellIdFromPoint(x, y) {
    const el = document.elementFromPoint(x, y);
    if (!el) return -1;
    const cell = el.closest('[data-tr-cell]');
    if (!cell || !container || !container.contains(cell)) return -1;
    const id = parseInt(cell.getAttribute('data-tr-cell'), 10);
    return Number.isNaN(id) ? -1 : id;
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
    dragging = true;
    lastCellId = id;
    ev.preventDefault();
    try { container.setPointerCapture?.(ev.pointerId); } catch { /* unsupported */ }
    invoke('OnDragStart', id);
}

function onPointerMove(ev) {
    if (!dragging) return;
    const id = cellIdFromPoint(ev.clientX, ev.clientY);
    if (id < 0 || id === lastCellId) return;
    lastCellId = id;
    ev.preventDefault();
    invoke('OnDragEnter', id);
}

function endDrag(ev) {
    if (!dragging) return;
    dragging = false;
    lastCellId = -1;
    try { container.releasePointerCapture?.(ev.pointerId); } catch { /* unsupported */ }
    invoke('OnDragEnd');
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
}

export function dispose() {
    if (container) {
        container.removeEventListener('pointerdown', onPointerDown);
        container.removeEventListener('pointermove', onPointerMove);
        container.removeEventListener('pointerup', endDrag);
        container.removeEventListener('pointercancel', endDrag);
    }
    container = null;
    dotNetRef = null;
    dragging = false;
    lastCellId = -1;
}
