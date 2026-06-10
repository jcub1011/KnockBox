// Global physical-keyboard interop for Spardle.
// Blazor calls register() once when the playing page mounts, unregister() on dispose.

let handler = null;
let activeRef = null;

export function register(dotNetRef) {
    unregister();
    activeRef = dotNetRef;
    handler = (ev) => {
        if (!activeRef) return;
        if (ev.ctrlKey || ev.metaKey || ev.altKey) return;
        const target = ev.target;
        if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable)) {
            return;
        }
        let key = ev.key;
        if (key === 'Enter') {
            key = 'ENTER';
        } else if (key === 'Backspace') {
            key = 'BACKSPACE';
        } else if (key.length === 1 && /^[a-zA-Z]$/.test(key)) {
            key = key.toLowerCase();
        } else {
            return;
        }
        ev.preventDefault();
        // Defer the .NET call out of the synchronous keydown handler. On WASM a keydown that fires
        // while Blazor is mid-render (e.g. an opponent's projection re-rendering the grid) would
        // reenter the locked heap — "Assertion failed - heap is currently locked". setTimeout(…,0)
        // lets the current render settle first. (Server circuits were immune; this is WASM-only.)
        const ref = activeRef;
        setTimeout(() => {
            try {
                ref.invokeMethodAsync('OnPhysicalKey', key);
            } catch (err) {
                // Ref was disposed between dispatches; ignore.
            }
        }, 0);
    };
    document.addEventListener('keydown', handler);
}

export function unregister() {
    if (handler) {
        document.removeEventListener('keydown', handler);
        handler = null;
    }
    activeRef = null;
}
