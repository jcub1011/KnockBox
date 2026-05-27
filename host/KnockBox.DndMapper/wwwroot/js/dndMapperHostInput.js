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

// Normalize browser e.key quirks so the stored Key and the streamed held
// set use identical labels — otherwise a "Space"-named rule never matches
// the live set which contains " ". Capture and onKeyDown MUST agree, so
// both go through this. Add new aliases here as you encounter them.
function normalizeKey(key) {
    if (key === " ") return "Space";
    return key;
}

function onKeyDown(e) {
    // Use e.key (logical) rather than e.code (physical) so rules say
    // "Space" / "Shift" not "ShiftLeft". Browsers normalize this.
    if (!e.key) return;
    const key = normalizeKey(e.key);
    if (held.has(key)) return;
    held.add(key);
    scheduleFlush();
}

function onKeyUp(e) {
    if (!e.key) return;
    if (!held.delete(normalizeKey(e.key))) return;
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

// One-shot key capture for the loaded-dice "set key" button. Returns a
// promise resolving to the next key the host presses (e.key, logical
// form — same shape HostKeyHeldCondition stores). Esc resolves to null
// so the caller can treat it as a cancel. Uses the capture phase + stop-
// Propagation so the streaming onKeyDown above doesn't ALSO record the
// pressed key in the held set — the binding press is transient.
let captureResolver = null;

function onCaptureKey(e) {
    if (!e.key) return;
    e.preventDefault();
    e.stopPropagation();
    const key = e.key === "Escape" ? null : normalizeKey(e.key);
    finalizeCapture(key);
}

function finalizeCapture(key) {
    if (captureResolver === null) return;
    const resolve = captureResolver;
    captureResolver = null;
    window.removeEventListener("keydown", onCaptureKey, true);
    resolve(key);
}

export function captureNext() {
    // A second captureNext call before the first resolved cancels the
    // first — the UI only exposes one listening button at a time, but
    // be defensive about navigation races.
    if (captureResolver !== null) finalizeCapture(null);
    return new Promise((resolve) => {
        captureResolver = resolve;
        window.addEventListener("keydown", onCaptureKey, true);
    });
}

export function cancelCapture() {
    finalizeCapture(null);
}
