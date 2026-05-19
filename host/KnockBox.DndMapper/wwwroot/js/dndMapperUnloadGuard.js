// Tiny module that attaches/detaches a beforeunload handler so the browser
// shows its built-in "Leave site?" confirmation while a save is pending. The
// returned string is ignored by modern browsers but is required to trigger the
// prompt.

let handler = null;

export function enable() {
    if (handler) return;
    handler = (e) => {
        e.preventDefault();
        // Modern browsers ignore the returned string but require returnValue set.
        e.returnValue = "A save is still in progress.";
        return e.returnValue;
    };
    window.addEventListener("beforeunload", handler);
}

export function disable() {
    if (!handler) return;
    window.removeEventListener("beforeunload", handler);
    handler = null;
}
