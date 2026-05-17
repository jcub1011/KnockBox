/**
 * DnD Mapper panel collapse — click a .dndm-panel-header to toggle a
 * .dndm-panel--collapsed class on its containing .dndm-panel. Stays purely
 * CSS-driven so Blazor re-renders don't reset state (the class lives on the
 * DOM element until the panel itself is removed).
 *
 * Skips toggling when the click landed on an interactive child of the header
 * (buttons, inputs, selects, etc.) so per-header actions like "+ NPC" or
 * "Settings ⚙" keep firing without folding the panel.
 */

let installed = false;

function isInteractiveDescendant(target, header) {
    // Walk from the click target up to (but not including) the header. If we
    // encounter an interactive element along the way, treat the click as an
    // action click rather than a collapse-toggle.
    let el = target;
    while (el && el !== header) {
        const tag = el.tagName;
        if (tag === 'BUTTON' || tag === 'INPUT' || tag === 'SELECT'
            || tag === 'TEXTAREA' || tag === 'A' || tag === 'LABEL') {
            return true;
        }
        if (el.getAttribute && el.getAttribute('role') === 'button') return true;
        el = el.parentElement;
    }
    return false;
}

function onDocClick(e) {
    const header = e.target.closest('.dndm-panel-header');
    if (!header) return;
    if (e.target !== header && isInteractiveDescendant(e.target, header)) return;
    const panel = header.closest('.dndm-panel');
    if (!panel) return;
    panel.classList.toggle('dndm-panel--collapsed');
}

export function ensureInstalled() {
    if (installed) return;
    installed = true;
    document.addEventListener('click', onDocClick);
}

export function dispose() {
    if (!installed) return;
    installed = false;
    document.removeEventListener('click', onDocClick);
}
