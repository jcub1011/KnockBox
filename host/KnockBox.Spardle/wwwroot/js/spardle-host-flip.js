const caches = new Map();

export function reorder(rootSelector, itemSelector, keyAttr, durationMs, revision) {
    const root = document.querySelector(rootSelector);
    if (!root) return;

    let cache = caches.get(rootSelector);
    if (!cache || cache.revision !== revision) {
        cache = { revision, positions: new Map() };
        caches.set(rootSelector, cache);
    }

    const current = new Map();
    for (const el of root.querySelectorAll(itemSelector)) {
        const key = el.getAttribute(keyAttr);
        if (!key) continue;
        const r = el.getBoundingClientRect();
        current.set(key, { el, x: r.left, y: r.top });
    }

    for (const [key, pos] of current) {
        const old = cache.positions.get(key);
        if (!old) continue;
        const dx = old.x - pos.x;
        const dy = old.y - pos.y;
        if (dx === 0 && dy === 0) continue;
        pos.el.style.transition = 'none';
        pos.el.style.transform = `translate(${dx}px, ${dy}px)`;
        void pos.el.offsetWidth;
        pos.el.style.transition = `transform ${durationMs}ms cubic-bezier(0.16, 1, 0.3, 1)`;
        pos.el.style.transform = '';
    }

    const next = new Map();
    for (const [k, p] of current) next.set(k, { x: p.x, y: p.y });
    cache.positions = next;
}

export function clear(rootSelector) {
    caches.delete(rootSelector);
}
