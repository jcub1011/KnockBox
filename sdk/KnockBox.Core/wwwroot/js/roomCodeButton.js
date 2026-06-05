/**
 * Finds the bottom edge (in viewport pixels) of the top header bar that contains
 * the room-code button, so the enlarged popup can be positioned below it and leave
 * the header (with the game's name) visible.
 *
 * Walks up the ancestor chain from the button and picks the tallest ancestor that is
 * anchored to the very top of the viewport but is short relative to the page — i.e.
 * the header bar, not the full-page content container.
 *
 * @param {HTMLElement} buttonEl - The room-code button element.
 * @returns {number} The header's bottom offset in pixels, or 0 if none is found.
 */
export function getHeaderBottom(buttonEl) {
    if (!buttonEl) return 0;

    const viewportHeight = window.innerHeight;
    let headerBottom = 0;
    let el = buttonEl;

    while (el && el !== document.body && el !== document.documentElement) {
        const rect = el.getBoundingClientRect();

        // A header bar touches the top of the viewport and is short relative to the page.
        const isTopAnchored = rect.top <= 4;
        const isBar = rect.height > 0 && rect.height < viewportHeight * 0.4;

        if (isTopAnchored && isBar && rect.bottom > headerBottom) {
            headerBottom = rect.bottom;
        }

        el = el.parentElement;
    }

    return Math.round(headerBottom);
}
