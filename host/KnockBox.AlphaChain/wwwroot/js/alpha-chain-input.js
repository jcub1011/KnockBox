// Client-owned word input for Alpha Chain.
//
// Why this exists: binding the word field with @bind:event="oninput" round-trips every
// keystroke to the Blazor Server circuit, and the component re-renders constantly (1Hz shot
// clock ticks + state-change events). Those re-renders race the user's keystrokes over the
// wire and clobber / flicker fast typing. So the <input> is owned entirely by the client —
// Blazor never sets its `value` — and we read the LIVE DOM value only at submission time.
//
// Two submit paths, both reading the live value (never the stale server mirror):
//   • Enter key                          -> OnWordSubmitted
//   • client-side deadline timer fires   -> OnWordSubmitted (auto-submit before the server
//                                           shot clock expires, so typed-but-not-Entered
//                                           text is never discarded)
// Blur commits the current draft to the server (OnDraftCommitted) as a resilience mirror —
// it does NOT play the word (clicking a card mid-turn must not submit prematurely).

const inputs = new Map();

export function register(id, el, dotNetRef) {
    unregister(id);
    if (!el) return;

    const state = { el, dotNetRef, timeoutId: null };
    inputs.set(id, state);

    state.keyHandler = (ev) => {
        // Feedback Loop silence: swallow every keystroke (incl. Enter) while locked.
        if (state.silenced) {
            ev.preventDefault();
            return;
        }
        if (ev.key === 'Enter') {
            ev.preventDefault();
            notify(state, 'OnWordSubmitted', el.value);
        }
    };
    state.blurHandler = () => {
        // Defer the .NET call out of the synchronous blur callback. Under WASM, a blur fired by
        // Blazor's own render edit (e.g. the input being `disabled` on a turn change) would
        // otherwise call back into .NET while the runtime heap is locked mid-render — the
        // "Assertion failed - heap is currently locked" error. OnDraftCommitted is a
        // fire-and-forget resilience mirror, so running it a tick later is harmless. (On the old
        // Blazor Server circuit the call was always a remote async hop, so this never bit.)
        const value = el.value;
        setTimeout(() => notify(state, 'OnDraftCommitted', value), 0);
    };

    el.addEventListener('keydown', state.keyHandler);
    el.addEventListener('blur', state.blurHandler);
}

// Arm (or re-arm) a client-side deadline. `remainingMs < 0` disarms (e.g. not your turn).
// The auto-submit fires `leadMs` before the server deadline to win the timeout race.
export function armDeadline(id, remainingMs, leadMs) {
    const state = inputs.get(id);
    if (!state) return;
    clearDeadline(state);
    if (remainingMs == null || remainingMs < 0) return;

    const lead = leadMs == null ? 400 : leadMs;
    const delay = Math.max(0, remainingMs - lead);
    state.timeoutId = setTimeout(() => {
        state.timeoutId = null;
        const value = state.el ? state.el.value : '';
        if (value && value.trim().length > 0) {
            notify(state, 'OnWordSubmitted', value);
        }
    }, delay);
}

// Feedback Loop: lock the input for `ms` at the start of a turn — typing and Enter are swallowed,
// the field reads "silenced…", then it unlocks and focuses. The shot clock keeps running (the
// penalty). The `disabled` attribute is Blazor-owned, so we use readOnly + a flag instead.
export function silence(id, ms) {
    const state = inputs.get(id);
    if (!state || !state.el) return;
    clearSilence(state);

    const el = state.el;
    state.silenced = true;
    state.silencedPlaceholder = el.getAttribute('placeholder') || '';
    el.value = '';
    el.readOnly = true;
    el.classList.add('ac-word-input-silenced');
    el.setAttribute('placeholder', 'silenced…');

    state.silenceId = setTimeout(() => {
        state.silenceId = null;
        state.silenced = false;
        el.readOnly = false;
        el.classList.remove('ac-word-input-silenced');
        el.setAttribute('placeholder', state.silencedPlaceholder || '');
        try { el.focus(); } catch (err) { /* element detached; ignore */ }
    }, ms);
}

export function focus(id) {
    const state = inputs.get(id);
    if (state && state.el) {
        try {
            state.el.focus();
            const len = state.el.value.length;
            state.el.setSelectionRange(len, len);
        } catch (err) { /* element detached; ignore */ }
    }
}

export function clear(id) {
    const state = inputs.get(id);
    if (state && state.el) state.el.value = '';
}

export function getValue(id) {
    const state = inputs.get(id);
    return state && state.el ? state.el.value : '';
}

// Horizontally centre the local player's item (.ac-lbm-me) within the mobile leaderboard strip.
// Uses scrollTo (not scrollIntoView) so the page never scrolls vertically; a no-op when the
// container is hidden (display:none on desktop) or the item is absent.
export function centerMe(container) {
    if (!container) return;
    const me = container.querySelector('.ac-lbm-me');
    if (!me) return;
    const left = me.offsetLeft - (container.clientWidth - me.clientWidth) / 2;
    container.scrollTo({ left, behavior: 'smooth' });
}

export function unregister(id) {
    const state = inputs.get(id);
    if (!state) return;
    clearDeadline(state);
    clearSilence(state);
    if (state.el) {
        if (state.keyHandler) state.el.removeEventListener('keydown', state.keyHandler);
        if (state.blurHandler) state.el.removeEventListener('blur', state.blurHandler);
    }
    inputs.delete(id);
}

function clearDeadline(state) {
    if (state.timeoutId != null) {
        clearTimeout(state.timeoutId);
        state.timeoutId = null;
    }
}

function clearSilence(state) {
    if (state.silenceId != null) {
        clearTimeout(state.silenceId);
        state.silenceId = null;
    }
    state.silenced = false;
}

function notify(state, method, arg) {
    try {
        state.dotNetRef.invokeMethodAsync(method, arg);
    } catch (err) {
        // Ref disposed between dispatches (circuit teardown); ignore.
    }
}
