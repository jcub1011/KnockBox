# Tracery — Tuning Surface

Every balance lever lives in `TracerySettings` (`Models/TracerySettings.cs`) — none are baked into
the engine, solver, scorer, or generator. Hosts edit the exposed subset live in the lobby
(`Components/TracerySettingsPanel.razor`); the rest are tunable in code/JSON and ride the same
record. Settings persist to the host's browser `localStorage`, so changes survive a refresh.

This file documents each tunable, its default, the range the panel clamps it to (where applicable),
and its intended effect, so playtesters can retune without touching code (GDD §10).

## Grid

| Setting | Default | Panel range | Effect |
|---|---|---|---|
| `GridWidth` | 4 | 3–8 | Board width in tiles. Larger boards hold more words but take longer to scan. |
| `GridHeight` | 4 | 3–8 | Board height in tiles. 8×8 is the largest supported grid (the solver perf bound is set against it). |
| `MinWordLength` | 4 | 3–8 | Shortest word that can be banked. Higher = harder, fewer findable words. The dictionary trie is built once at the global floor (3); per-round filtering applies this value. |

## Rounds & timing

| Setting | Default | Panel range | Effect |
|---|---|---|---|
| `RoundTimer` | 90 s | 15–600 s, or Unlimited | Per-round play time. `TimeSpan.Zero` = unlimited (host-advanced). Shorter = more frantic; longer rewards thoroughness. |
| `TotalRounds` | 3 | 1–20 | Number of timed rounds before final standings. |
| `TransitionDuration` | 5 s | 2–15 s | Pacing of the round-1 "get ready" intro before the first grid appears. |
| `IntermissionDuration` | 10 s | — | Length of the single post-round intermission (reveal: words found, scoring, standings, next-round indicator) before the next round begins. |

## Scoring — length-bonus curve (GDD §5.2)

| Setting | Default | Effect |
|---|---|---|
| `LengthBonusTable` | `[0,0,0,0,0,1,3,6,10,15,21]` | Superlinear bonus added on top of base score, indexed by word length. Index `[len]`; lengths below the minimum are 0, lengths ≥ the last index clamp to the final ("10+") entry. Flatten it to reward length less; steepen it to make long words decisive. |

## Scoring — rare-letter bonus (GDD §5.3)

| Setting | Default | Effect |
|---|---|---|
| `RareLetterBonusEnabled` | `true` | Master toggle for the rare-letter layer. Off → no rare bonus and the reveal's "rarest letters" beat is empty. |
| `RareLetterBonusTable` | K,F,H,V,W,Y → +1; J,X → +3; Q,Z → +5 | Per-occurrence bonus keyed by upper-case letter (repeats count). Retune to change which letters feel valuable. |

> The generator's notion of which letters are "rare" for the generation quality gate
> (`RequireRareLetterWord`) comes from `LetterDistribution.RareLetters` (f,h,j,k,q,v,w,x,y,z) — the
> same letters as the keys of the scoring table above, so a word that earns a rare-letter bonus is
> exactly one that satisfies the gate. Retune both together if you change which letters are rare.

## Scoring — unique-find multiplier (GDD §5.4)

| Setting | Default | Effect |
|---|---|---|
| `UniqueFindBonusEnabled` | `true` | Toggle for the unique-find multiplier. |
| `UniqueFindMultiplier` | 1.5 | Panel range 1.0–5.0. Applied to the *whole* word score when no other player banked the same word this round, rounded half-away-from-zero. Higher rewards finding words others miss; 1.0 (or disabled) makes finds worth the same shared or not. |
| `ShowTheoreticalMax` | `true` | Display-only. Shows the board's theoretical maximum (every findable word banked as a unique find) on the reveal. Never affects scoring. |

## Host role

| Setting | Default | Effect |
|---|---|---|
| `HostPlaysAlong` | `false` | When other players are present: `false` makes the host the shared display-only screen; `true` lets the host play as a participant too. With no other players the host always plays. |

## Generation quality bar (GDD §6)

A board is accepted only if it clears this bar within the attempt cap; otherwise a seed fallback
plants a known dictionary word along a legal path so no round is ever dead.

| Setting | Default | Effect |
|---|---|---|
| `MinFindableWords` | 0 (→ engine default) | Minimum findable words a board must have. `0` defers to the engine default: `max(8, round(area × 0.75))` — scales the floor with grid area. Raise to force word-richer boards (more generation attempts / more fallbacks). |
| `MinLongWordLength` | 7 | A board must contain at least one findable word of this length (the "big find"). Clamped to what the grid can physically hold. Also the length the fallback plants. |
| `RequireRareLetterWord` | `true` | Require at least one findable word using a rare-letter tile. Off → boards may have no rare words. |
| `MaxGenerationAttempts` | 0 (→ engine default 50) | Generate-and-test attempts before falling back to a seeded board. `0` defers to the engine default (50). Higher = more chances to clear a strict bar before the fallback fires, at more CPU per round. |

## Performance note

The solver runs once per round and up to `MaxGenerationAttempts` times per generated board, so
generate-and-test is the dominant cost (GDD §9). Prefix pruning keeps an 8×8 solve fast; see
`SolverPerformanceTests` for the bounded-time guard. The dictionary trie is built **once** and cached
on the singleton engine — never per round or per lobby.
