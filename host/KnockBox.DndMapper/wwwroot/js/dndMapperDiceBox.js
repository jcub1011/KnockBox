// dndMapperDiceBox.js
// Thin wrapper around @3d-dice/dice-box-threejs. One DiceBox instance per
// rolling user so concurrent rolls can carry different colors. Each box's
// container <div> is appended to the supplied overlay element; the dice-box
// library mounts its own canvas inside. Physics is independent per client —
// the authoritative outcome is forced via the library's "@N" notation so the
// rolled face matches the server result regardless of local simulation.

import DiceBox from "/_content/KnockBox.DndMapper/lib/dice-box-threejs/dice-box.es.js";

const FADE_MS = 3000;
const boxes = new Map(); // userId -> { box, container, color, fontColor, fadeTimer, currentRollId, dotnet, ready }

function safeId(userId) {
    return userId.replace(/[^a-zA-Z0-9_-]/g, "_");
}

function customColorset(userId, color, fontColor) {
    return {
        name: `kb-${safeId(userId)}`,
        background: color,
        foreground: fontColor,
        texture: "none",
        material: "plastic",
    };
}

async function ensureBox(overlay, userId, color, fontColor) {
    let entry = boxes.get(userId);
    if (entry && entry.color === color && entry.fontColor === fontColor) {
        return entry;
    }

    if (entry) {
        try { entry.box.clearDice(); } catch (_) { /* ignore */ }
        if (entry.fadeTimer) clearTimeout(entry.fadeTimer);
        if (entry.container && entry.container.parentNode) entry.container.parentNode.removeChild(entry.container);
        boxes.delete(userId);
    }

    const container = document.createElement("div");
    container.id = `dndm-dice-${safeId(userId)}`;
    container.style.cssText = "position:absolute;inset:0;pointer-events:none;";
    overlay.appendChild(container);

    const box = new DiceBox(`#${container.id}`, {
        assetPath: "/_content/KnockBox.DndMapper/dice/",
        theme_customColorset: customColorset(userId, color, fontColor),
        theme_surface: "green-felt",
        theme_material: "plastic",
        baseScale: 100,
        gravity_multiplier: 600,
        // Dice-box throw velocity is randomized as (Math.random() + 3) * n *
        // strength (see dice-box.es.js ~L17110), so strength scales both ends
        // of the random range. Doubling it from 1→2 lifts the minimum throw
        // velocity to 2× its baseline so dice actually travel across the
        // tray instead of barely tipping off the spawn point.
        strength: 2,
        shadows: true,
        sounds: false,
        onRollComplete: null, // assigned per-roll below so we can capture rollId.
    });

    await box.initialize();

    entry = {
        box,
        container,
        color,
        fontColor,
        fadeTimer: null,
        currentRollId: null,
        dotnet: null,
        ready: true,
    };
    boxes.set(userId, entry);
    return entry;
}

export async function rollFor(overlay, userId, color, fontColor, notation, dotnet, rollId) {
    if (!overlay || !notation) return;
    const entry = await ensureBox(overlay, userId, color, fontColor);
    entry.dotnet = dotnet;

    // Interrupt any in-flight roll for this user: clear the pending fade and
    // wipe currently-visible dice. The .NET tracker is told to settle the
    // previous roll separately (DiceCanvas handles that before invoking us).
    if (entry.fadeTimer) {
        clearTimeout(entry.fadeTimer);
        entry.fadeTimer = null;
    }
    if (entry.currentRollId) {
        try { entry.box.clearDice(); } catch (_) { /* ignore */ }
    }

    entry.currentRollId = rollId;
    entry.box.onRollComplete = async () => {
        const settledRollId = entry.currentRollId;
        if (settledRollId !== rollId) return; // a newer roll already replaced us.
        entry.fadeTimer = setTimeout(() => {
            try { entry.box.clearDice(); } catch (_) { /* ignore */ }
            entry.fadeTimer = null;
            if (entry.currentRollId === rollId) entry.currentRollId = null;
        }, FADE_MS);
        try {
            await dotnet.invokeMethodAsync("OnRollSettled", userId, settledRollId);
        } catch (_) { /* circuit gone — fine */ }
    };

    try {
        await entry.box.roll(notation);
    } catch (e) {
        // If parsing or rolling threw, immediately settle so the log isn't
        // permanently stuck hiding this roll.
        if (entry.currentRollId === rollId) entry.currentRollId = null;
        try { await dotnet.invokeMethodAsync("OnRollSettled", userId, rollId); } catch (_) { /* ignore */ }
        // eslint-disable-next-line no-console
        console.warn("dndMapperDiceBox.roll failed", e);
    }
}

export function disposeAll() {
    for (const [, e] of boxes) {
        // Null the per-roll callback first so any in-flight physics tick
        // that fires onRollComplete after dispose can't reach the now-
        // disposed DotNetObjectReference (ObjectDisposedException noise).
        try { if (e.box) e.box.onRollComplete = null; } catch (_) { /* ignore */ }
        try { e.box.clearDice(); } catch (_) { /* ignore */ }
        if (e.fadeTimer) clearTimeout(e.fadeTimer);
        if (e.container && e.container.parentNode) e.container.parentNode.removeChild(e.container);
        e.dotnet = null;
        e.currentRollId = null;
    }
    boxes.clear();
}
