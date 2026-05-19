# Display View + Fog of War — Implementation Plans

Two features being pulled forward from the v1.x deferred list (`dnd-mapper-gdd.md` §13 + §15):

1. **Display System** — a public, read-only URL the host can open on a TV or projector. Lives at `/room/dnd-mapper/{ObfuscatedRoomCode}/display`. No controls, no host secrets leak through.
2. **Fog of War** — host paints a per-cell fog mask per map. Players and the display view see fog as opaque (and the tokens / images behind it disappear). The host sees fog as a translucent overlay so it stays editable.

## Milestones

| # | File | Summary |
|---|------|---------|
| M01 | [M01-display-completion-and-sharing.md](M01-display-completion-and-sharing.md) | Finish the existing `DndMapperDisplay.razor` scaffold, add dedicated CSS, extract a `DisplayProjection` helper, and add a host-side "Open display view" button that copies the URL to the clipboard. |
| M02 | [M02-fog-state-and-engine.md](M02-fog-state-and-engine.md) | Add the per-cell fog mask to `Map`, write host-only engine verbs (`PaintFogAsync` / `FillMapWithFogAsync` / `ClearAllFogAsync`), and persist through `DndMapperLibraryService`. UI is deferred to M03. |
| M03 | [M03-fog-host-ui.md](M03-fog-host-ui.md) | Host paint / erase / brush-size / fill / clear UX inside `MapCanvas`, plus a new `HostFogPanel` in the left rail. Host renders fog as a translucent overlay. |
| M04 | [M04-fog-player-and-display-visibility.md](M04-fog-player-and-display-visibility.md) | Player + display rendering: opaque fog, tokens-behind-fog filter, images-fully-covered-by-fog filter. Extends `TokenVisibilityFilter` and `DisplayProjection`. |
| M05 | [M05-end-to-end-verification.md](M05-end-to-end-verification.md) | Three-browser verification matrix (host + player + display) covering the joint behavior of fog, hidden tokens, map switching, save/reload, and the missing-room UX. |

## Design decisions confirmed before planning

- **FoW granularity**: per grid cell toggle (not free-form brush, not regions). One bit per cell, packed into `byte[]`.
- **Share affordance**: host UI gets an "Open display view" button — also copies the absolute URL to the clipboard.
- **Missing-room UX**: keep the existing static "Room not found" message; no auto-retry. Tabs opened *before* a lobby starts must be refreshed manually.

## Conventions inherited from `../Implementation/M0X-*.md`

- All state mutations go through `state.Execute*`. Engine verbs return `Result` / `ValueResult<T>`. Setters on the state class stay `internal`.
- Host-only verbs guard with the same caller check existing map verbs use (caller's `Id == state.Host.Id`).
- Razor pages inherit `DisposableComponent` (display page is the exception — it doesn't go through `LobbyPageBase` because it has no session).
- New JS modules live under `wwwroot/js/` and are imported lazily from the consuming component.
- Tests live in `host/KnockBox.DndMapperTests/Unit/*Tests.cs`. Pure helpers prefer plain unit tests; bUnit only when a component's logic isn't extractable.
