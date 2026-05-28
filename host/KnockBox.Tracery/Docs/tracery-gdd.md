# Tracery — Game Design Document

*Version 0.1 — Draft*

---

## 1. Overview

**Tracery** is a real-time, competitive word-finding party game. All players are presented with the *same* randomized grid of letters and race, within a fixed time limit, to trace as many valid words as possible while maximizing their score. It is designed in the Jackbox mold: a shared screen plus phones-as-controllers, played by people in the same room.

The game sits in the Boggle / Word Hunt lineage but distinguishes itself through three deliberate choices: a **unique-find scoring bonus** that rewards seeing the grid differently rather than just having the deepest vocabulary, a **generate-and-test board pipeline** that guarantees every round is rich and worth playing, and a **superlinear scoring curve** tuned to make the hardest finds feel as rewarding as they are.

### High concept

> The same grid for everyone. The same clock. Whoever sees the most — and sees what no one else does — wins.

### Design pillars

1. **Shared board, individual race.** Every player works the identical grid, so outcomes reflect skill and perception, not luck of the draw.
2. **Reward seeing differently, not just knowing more.** Unique finds are worth more than common ones. The clever path beats the obvious one.
3. **Every board is a good board.** No dead rounds. Generation guarantees a satisfying spread of words, including at least one big find to chase.
4. **The reveal is the payoff.** The end-of-round screen is a spectacle, not a spreadsheet.

---

## 2. Target Audience & Platform

**Audience.** People who enjoy word games and want a competitive, social version of one. Tracery deliberately does *not* sand down its skill ceiling to accommodate players who dislike word games; the design assumes an audience that finds vocabulary and pattern-scanning fun. The unique-find mechanic provides natural compression of the skill gap *among* word-game enthusiasts without compromising depth.

**Platform & model.** Local, same-room multiplayer in the Jackbox style:

- A **host display** (TV, laptop, or shared browser window) shows the lobby, the shared grid during play, and the reveal.
- **Players join from their phones** using a room code; the phone is the controller and shows each player their own copy of the grid for tracing.
- Recommended player count: **2–8**.

---

## 3. Core Gameplay Loop

1. The host starts a round. A randomized grid is generated and shown identically to every player.
2. A countdown timer begins.
3. Players trace words by drawing connecting lines across the letters on their phones.
4. Valid words are scored and banked; invalid or too-short traces are rejected with feedback.
5. When the timer expires, input locks.
6. The **reveal** plays out on the host screen: notable words, words nobody found, scores.
7. Repeat for the configured number of rounds, then show final standings.

---

## 4. Rules of Word Tracing

A word is formed by drawing a continuous line connecting adjacent letter cells.

- **Adjacency is 8-way.** A cell connects to its orthogonal and diagonal neighbors — up to eight surrounding cells.
- **The line may change direction freely.** It does not need to be straight or follow a single diagonal; it can bend at every step.
- **No gaps.** Each letter in the path must be directly adjacent to the previous one.
- **No self-intersection within a single word.** A cell may not be revisited *while tracing one word*. (This is what keeps long words genuinely hard to find.)
- **Cells are reusable across different words.** A cell consumed by one word is fully available for any other word. Finding a word does not "use up" the grid.
- **Minimum word length is 4.** Words of three letters or fewer do not count. This is intentional: the sub-4 space is dominated by dictionary-legal filler (the *qi / za / xu / jo* tier) that rewards list-exploitation rather than word-finding, and excluding it removes a spam strategy.

### Validation

- Words are validated against the project's chosen word list (decided).
- A word scores **once per player per round**, regardless of how many distinct paths spell it. Re-submitting an already-banked word does nothing.

---

## 5. Scoring System

A word's score is built in three additive/multiplicative layers. *All constants below are starting proposals to be confirmed through playtesting.*

### 5.1 Base score — word length

Base points equal the word's length. An 8-letter word is worth 8 base points; a 4-letter word, 4.

### 5.2 Length bonus — superlinear

Because self-intersection is banned, longer words represent real spatial-scanning skill, so the total reward curve escalates with length rather than staying flat. A length-milestone bonus is added on top of the base:

| Word length | Base | Length bonus | Total (base + bonus) |
|-------------|-----:|-------------:|---------------------:|
| 4           | 4    | 0            | 4                    |
| 5           | 5    | +1           | 6                    |
| 6           | 6    | +3           | 9                    |
| 7           | 7    | +6           | 13                   |
| 8           | 8    | +10          | 18                   |
| 9           | 9    | +15          | 24                   |
| 10+         | n    | +21 (and up) | n + 21 …             |

The curve should feel like Boggle's escalation — a 9-letter find is a small triumph and should pay like one.

### 5.3 Rare-letter bonus

Words containing infrequent letters earn additional points, applied per qualifying letter used (Scrabble-style values are a reasonable starting point):

| Letters         | Bonus per occurrence |
|-----------------|---------------------:|
| K, F, H, V, W, Y| +1                   |
| J, X            | +3                   |
| Q, Z            | +5                   |

### 5.4 Unique-find bonus (signature mechanic)

If a word was found by **only one player** in the round, that player's score for the word is multiplied (proposed **×1.5**, rounded). A multiplier rather than a flat bonus is used so the reward scales with the word's difficulty — a unique 9-letter rare-letter word is far more impressive than a unique 4-letter word, and should pay accordingly.

This is the heart of Tracery's competitive identity. It rewards finding what others overlook, discourages everyone grinding the same obvious common words, and further dampens any value in junk words (which multiple players tend to stumble into anyway).

### 5.5 Worked example

The word **QUARTZ** (6 letters), found by only one player:

- Base: 6
- Length bonus: +3 → 9
- Rare-letter: Q (+5) + Z (+5) → 19
- Unique ×1.5 → **≈29 points**

---

## 6. Grid Generation

The goal is that *every board is a good board* — no dead rounds, always at least one big word to chase. Generation combines two techniques:

### 6.1 Weighted letter distribution

Letters are sampled from a frequency-weighted distribution (English frequency or curated Boggle-style weights) rather than uniformly. This makes the overwhelming majority of boards naturally word-rich on the first try, keeping rejection rates low.

### 6.2 Generate-and-test

Each candidate board is fed to the solver and accepted only if it clears a quality bar tuned on *distribution*, not merely total count. Proposed acceptance criteria (tunable):

- A minimum number of findable words, scaled to grid size.
- At least one findable word of length ≥ 7 (the "big find" to chase).
- Preferably at least one word using a rare-letter bonus tile.

Boards that fail are discarded and regenerated. Weighted generation plus this guardrail keeps the loop fast while guaranteeing quality.

> Optional fallback: for very small grid dimensions where random generation can struggle, seed a single guaranteed word along a valid path after randomization. Larger grids should rarely need this.

Because all players share one board, generation runs once per round on the host.

---

## 7. The Reveal

The end-of-round screen on the host display is the social payoff and should be paced for reactions, not just totals. Candidate beats to surface:

- The **longest word** anyone found, and who found it.
- The **highest-scoring word** of the round.
- **Words nobody found** — especially the long or rare-letter ones lurking on the board (sourced directly from the solver's complete word set).
- The **rarest letters** put to use.
- Each player's score and the running match standings.
- Optionally, the **theoretical maximum** score the board allowed, as a benchmark.

---

## 8. Settings & Configuration

Configurable in game settings (defaults proposed):

| Setting              | Default | Notes                                            |
|----------------------|---------|--------------------------------------------------|
| Grid dimensions      | 4×4     | Configurable; larger grids = longer, deeper rounds |
| Round timer          | 90 s    | Per round                                        |
| Rounds per match     | 3       |                                                  |
| Minimum word length  | 4       | Configurable, but 4 is the recommended floor     |
| Unique-find bonus    | On      | Multiplier value tunable                         |
| Rare-letter bonus    | On      | Value table tunable                              |

---

## 9. Technical Architecture (Design-Level)

### The solver is the keystone

A single grid solver underpins four distinct jobs, so it should be built well once:

1. **Generation testing** — scoring candidate boards against the quality bar.
2. **Runtime validation** — confirming each player's traced word is real, contiguous, non-self-intersecting, and ≥ minimum length.
3. **Scoring** — computing base, length, rare-letter, and unique-find layers.
4. **Reveal data** — producing the complete set of findable words so the host can show what nobody found and the theoretical maximum.

### Solver approach

- Load the dictionary into a **trie or DAWG**.
- Enumerate words via **depth-first search** over the grid with 8-way adjacency, tracking visited cells per path to enforce no self-intersection.
- Apply **prefix pruning**: abandon any path the instant its accumulated prefix is not a valid prefix in the dictionary. This keeps the search fast even on large, configurable grid dimensions — essential because generate-and-test runs the solver repeatedly.

### Runtime flow

- Board generation and the authoritative word set are computed host-side once per round.
- Player submissions are validated against grid rules and the dictionary; unique-find status is resolved after the round closes by comparing all players' banked word sets.

---

## 10. Open Questions & Risks

- **Name collision.** "Tracery" is also a well-known generative-grammar text tool (by Kate Compton) widely used in the games/generative-art and bot communities. It is a different product category, so not a legal conflict, but it may create SEO and discoverability friction in exactly the indie/dev circles the game would want to reach. Worth a deliberate decision before committing to branding.
- **Scoring constants.** All point values, the length-bonus curve, the rare-letter table, and the unique-find multiplier are starting proposals and should be tuned by playtest for feel and balance.
- **Dictionary edition specifics** (plurals, proper-noun policy, list version) are decided by the team; the design only assumes the list is authoritative and unambiguous.
- **Grid size vs. timer balance.** Larger grids with the same timer favor faster scanners; round length may need to scale with dimensions.

---

## 11. Future / Stretch Ideas

- Daily shared board ("everyone in the world gets the same grid today").
- Themed letter distributions or word lists.
- Spectator-friendly path replays on the reveal screen, animating how a winning word was traced.
