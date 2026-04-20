const clocks = new Map();

/**
 * Starts a client-side countdown clock that calls back to Blazor at ~10Hz.
 * @param {string} id - Unique clock instance ID.
 * @param {DotNetObjectReference} dotNetRef - Blazor component reference for callbacks.
 * @param {number} remainingMs - Milliseconds remaining (computed server-side).
 * @param {number} durationMs - Total duration in milliseconds.
 */
export function start(id, dotNetRef, remainingMs, durationMs) {
    stop(id);

    const endTime = Date.now() + remainingMs;
    const state = { dotNetRef, endTime, durationMs, intervalId: null };
    clocks.set(id, state);

    function tick() {
        const now = Date.now();
        const remaining = Math.max(0, state.endTime - now);
        const fraction = durationMs > 0 ? Math.min(remaining / durationMs, 1) : 0;

        state.dotNetRef.invokeMethodAsync('OnCountdownTick', fraction, remaining / 1000, durationMs / 1000)
            .catch(() => { });

        if (remaining <= 0) {
            clearInterval(state.intervalId);
            state.intervalId = null;
        }
    }

    tick();
    state.intervalId = setInterval(tick, 100);
}

/**
 * Stops and cleans up a countdown clock.
 * @param {string} id - Clock instance ID.
 */
export function stop(id) {
    const state = clocks.get(id);
    if (state) {
        if (state.intervalId != null) {
            clearInterval(state.intervalId);
        }
        clocks.delete(id);
    }
}
