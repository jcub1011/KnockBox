// Thin wrapper around the async Clipboard API so .NET can copy text via a
// single JS interop call. Rejects when the API isn't available (insecure
// context, missing permission, older browser) so the caller can fall back
// to "the new tab still opens" UX instead of pretending the copy succeeded.
export function copy(text) {
    if (typeof text !== "string") {
        return Promise.reject(new Error("copy() requires a string."));
    }
    if (navigator?.clipboard?.writeText) {
        return navigator.clipboard.writeText(text);
    }
    return Promise.reject(new Error("Clipboard API unavailable in this context."));
}

export function openTab(url) {
    if (typeof url !== "string" || url.length === 0) return;
    window.open(url, "_blank", "noopener,noreferrer");
}
