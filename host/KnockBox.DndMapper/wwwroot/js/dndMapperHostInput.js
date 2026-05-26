// Captures the host's keyboard state on a per-circuit basis and pushes
// debounced snapshots back to the .NET engine so HostKeyHeldCondition
// (loaded-dice rules) can match against currently-held keys.
//
// Lifecycle:
//   attach(dotNetRef, callbackName)  — wires window keydown/keyup listeners
//   detach()                          — removes listeners (call from Dispose)
//
// We send the snapshot when the set changes (debounced via micro-throttle)
// so a fast tap-and-release lands quickly without a 100 ms input lag, but a
// burst of modifier keys doesn't N-square the interop traffic.

let attached = false;
let dotNetRef = null;
let callbackName = null;
const held = new Set();
let pending = false;
let lastSentJson = "[]";

function onKeyDown(e) {
    // Use e.key (logical) rather than e.code (physical) so rules say
    // "Space" / "Shift" not "ShiftLeft". Browsers normalize this.
    if (!e.key) return;
    if (held.has(e.key)) return;
    held.add(e.key);
    scheduleFlush();
}

function onKeyUp(e) {
    if (!e.key) return;
    if (!held.delete(e.key)) return;
    scheduleFlush();
}

function onBlur() {
    // Lose focus ⇒ assume no keys held. Without this a rule like
    // "host holds Space ⇒ nat 1" would stick if the host alt-tabs away
    // while pressing the key.
    if (held.size === 0) return;
    held.clear();
    scheduleFlush();
}

function scheduleFlush() {
    if (pending) return;
    pending = true;
    // Coalesce same-microtask events; queueMicrotask fires before paint so
    // rules that race a roll submit usually see the latest state.
    queueMicrotask(flush);
}

async function flush() {
    pending = false;
    if (!attached || !dotNetRef || !callbackName) return;
    const arr = Array.from(held);
    arr.sort();
    const json = JSON.stringify(arr);
    if (json === lastSentJson) return;
    lastSentJson = json;
    try {
        await dotNetRef.invokeMethodAsync(callbackName, arr);
    } catch {
        // Circuit dropped or method not found — silently ignore. The
        // .NET-side .Dispose() will call detach() before tear-down.
    }
}

export function attach(ref, method) {
    if (attached) detach();
    dotNetRef = ref;
    callbackName = method;
    held.clear();
    lastSentJson = "[]";
    window.addEventListener("keydown", onKeyDown);
    window.addEventListener("keyup", onKeyUp);
    window.addEventListener("blur", onBlur);
    attached = true;
}

export function detach() {
    if (!attached) return;
    window.removeEventListener("keydown", onKeyDown);
    window.removeEventListener("keyup", onKeyUp);
    window.removeEventListener("blur", onBlur);
    attached = false;
    dotNetRef = null;
    callbackName = null;
    held.clear();
}
