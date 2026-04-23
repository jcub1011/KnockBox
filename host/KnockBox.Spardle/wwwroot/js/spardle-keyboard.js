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
        try {
            activeRef.invokeMethodAsync('OnPhysicalKey', key);
        } catch (err) {
            // Ref was disposed between dispatches; ignore.
        }
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
