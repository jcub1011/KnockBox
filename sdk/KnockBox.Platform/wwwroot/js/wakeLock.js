/**
 * Screen Wake Lock — keeps the device's screen from sleeping while the user is
 * on a lobby/game page. The browser releases the sentinel automatically when
 * the page becomes hidden, so we re-request on visibility/bfcache restore.
 *
 * iOS PWA note: pre-iOS 18.4, wake lock did not work in Home Screen Web Apps
 * (WebKit bug 254545). No client-side fix; affected users silently no-op.
 */

let sentinel = null;
let listenerController = null;
let releaseGeneration = 0;
// Coalesces concurrent request() calls onto a single in-flight wakeLock.request
// promise. Without this, two callers (e.g., acquire() + a visibilitychange
// re-acquire) racing on the initial request both see sentinel === null and
// would each create a sentinel, orphaning the loser.
let pendingRequest = null;
// iOS pre-18.4 PWAs expose navigator.wakeLock but reject every request() with
// NotAllowedError. Cache that so we don't spam the console on every render or
// visibilitychange. Cleared only by a page reload — version flips need one anyway.
let permanentlyUnsupported = false;

async function request() {
    if (sentinel !== null) return;
    if (pendingRequest !== null) { await pendingRequest; return; }
    if (typeof navigator === 'undefined' || !('wakeLock' in navigator)) return;
    if (permanentlyUnsupported) return;

    const requestedGeneration = releaseGeneration;
    pendingRequest = (async () => {
        let acquired;
        try {
            acquired = await navigator.wakeLock.request('screen');
        } catch (err) {
            if (err?.name === 'NotAllowedError') {
                permanentlyUnsupported = true;
                console.warn('[WakeLock] not permitted on this platform; suppressing further attempts.');
            } else {
                console.warn('[WakeLock] request failed:', err);
            }
            return;
        }

        if (requestedGeneration !== releaseGeneration) {
            // release() ran while we were awaiting the request — drop the new
            // sentinel rather than leaking it.
            try { await acquired.release(); } catch { /* swallow */ }
            return;
        }

        sentinel = acquired;
        sentinel.addEventListener('release', () => { sentinel = null; });
    })().finally(() => { pendingRequest = null; });

    await pendingRequest;
}

export async function acquire() {
    if (typeof navigator === 'undefined' || !('wakeLock' in navigator)) return;

    if (listenerController === null) {
        listenerController = new AbortController();
        const opts = { signal: listenerController.signal };

        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'visible' && sentinel === null) {
                request().catch(err => console.warn('[WakeLock] re-acquire failed:', err));
            }
        }, opts);

        // Safari restores from bfcache without firing visibilitychange.
        window.addEventListener('pageshow', e => {
            if (e.persisted && sentinel === null && document.visibilityState === 'visible') {
                request().catch(err => console.warn('[WakeLock] pageshow re-acquire failed:', err));
            }
        }, opts);
    }

    await request();
}

export async function release() {
    releaseGeneration++;

    if (listenerController !== null) {
        listenerController.abort();
        listenerController = null;
    }

    const local = sentinel;
    sentinel = null;
    if (local !== null) {
        try {
            await local.release();
        } catch (err) {
            console.warn('[WakeLock] release failed:', err);
        }
    }
}
