# Linked List — Game Design Document

**Version:** 0.1 (Draft)
**Type:** Digital party word game
**Status:** Concept / pre-production — v1 scope is human-Auditor only; automation deferred (§11)

---

## 1. Overview

**High concept.** Players build a chain of word pairs to travel from a *starting word* to a *destination word*. Each new pair must begin with the last word of the previous pair. A rotating human **Auditor** approves or rejects each submission, and their call is final. It's a word ladder crossed with improv comedy.

**Example chain** — start `DOG`, destination `WORK`:

| Step | Submission | Carried word | Auditor |
|------|------------|--------------|---------|
| 1 | DOG **HOUSE** | HOUSE | ✅ |
| 2 | HOUSE **WORK** | WORK | ✅ reached destination |

Reached in 2 accepted pairs. A looser Auditor might also accept playful pairs like `BALL HAIR` — that subjectivity is the point, not a bug.

**Genre.** Party / word / social.
**Players.** 3–10 (one Auditor + two or more players). Sweet spot 4–6.
**Round length.** ~3–10 minutes.
**Inspiration.** The word-association segment from the *Distractible* podcast.

---

## 2. Platform

Web-based, **room-code multiplayer** in the style of Jackbox. Each participant joins from their own device (phone, tablet, laptop) using a short code. An optional shared **Stage screen** (TV, projector, or screen-share) displays the live chain, timer, and score for the group.

This supports two contexts with one build:

- **In-person party** — players in a room, Stage on a TV.
- **Remote / online** — players on a call, Stage shared via screen-share.

(A solo / practice mode would need an automated judge and is out of scope for v1 — see §11.)

---

## 3. Design Pillars

1. **Fun over fairness.** The game is deliberately loose. The Auditor's subjectivity is entertainment, and the rules should never get in the way of a good round.
2. **Banter is the product.** Every rejection comes with a stated reason. Arguing with the Auditor is encouraged, not penalized.
3. **The software keeps score so humans can play.** Timing, guess counts, rejection caps, chain history, and loop detection are all automated. Human judgment is reserved for the one thing it's good at: deciding whether a pair "counts."
4. **Zero rules to host.** Anyone can be Auditor with no rules knowledge, because the rule is "do you buy it or not."

---

## 4. Core Gameplay Loop

1. The game presents a **start word** and a **destination word**.
2. The active player submits a **word pair** that begins with the current carried word (on turn one, the start word).
3. The **Auditor** approves or rejects, giving a quick reason.
   - **Approved:** the second word of the pair becomes the new carried word. Turn passes on.
   - **Rejected:** the player tries again, until they succeed or hit the **rejection cap** (see §7.3).
4. Repeat until a submission's second word equals the **destination word**.
5. The round ends; the software reports the score for the active mode (§5).

The software automatically tracks the carried word, the full chain, the guess count, the clock, and rejections — players and the Auditor only ever do the creative and judgment work.

---

## 5. Scoring Modes

The host picks one scoring mode per match.

### 5.1 Fewest Guesses (puzzle mode)

- Each **accepted** pair counts as **1 guess**.
- **Rejected** pairs do **not** count toward the guess total.
- Goal: reach the destination in the fewest accepted guesses.

Because rejections are free here, the **rejection cap** (§7.3) is the friction that stops players from brute-forcing the Auditor with endless spitballs.

### 5.2 Fastest Time (pressure mode)

- A per-turn **clock** runs while the active player is thinking and submitting.
- The clock **pauses during auditing** — Auditor deliberation never counts against players.
- **Rejected attempts still consume clock time.** This is the natural friction in timed mode: bad guesses cost you seconds, so the rejection cap matters less here but still applies.
- Goal: reach the destination in the lowest total elapsed time.

### 5.3 Mode comparison

| | Fewest Guesses | Fastest Time |
|---|---|---|
| What's measured | Accepted pairs | Elapsed thinking/submitting time |
| Cost of a rejection | None (capped) | Lost seconds |
| Feel | Thoughtful, optimization | Quick, high-pressure |
| Primary friction | Rejection cap | The clock |

---

## 6. The Auditor

The Auditor is a human player and the heart of the game. There is no automated judge in v1, and that's a deliberate choice rather than a shortcut. The set of valid English word pairs is effectively unbounded and always shifting — open compounds, slang, playful coinages — so any fixed dataset would end up rejecting pairs players *know* are fine and feel broken rather than strict. A human sidesteps that entirely and supplies the banter the game is built on. (An optional automated judge is considered as a future feature in §11.)

The active Auditor approves or rejects each submission and **must enter a short reason** when rejecting (banter fuel; shown on the Stage). The Auditor may be **strict or lenient** — that's their prerogative, and their decision is final with no formal appeals (heckling is welcome).

- **Persona dial (optional flavor).** At round start the Auditor can pick a persona — e.g. *Merciless Judge*, *Easy Mark*, *Pedant* — that sets a tone and doubles as an informal difficulty setting. Purely cosmetic; it changes vibe, not rules.
- **Rotation.** Because the Auditor can't play, the role **rotates each round** so nobody is stuck refereeing all night. Rotation order is automatic.

---

## 7. Detailed Rules

### 7.1 What counts as a "word pair"

The *intended* unit is a recognizable two-word pairing — a compound or common collocation (BASKET BALL, DOG HOUSE, HOUSE WORK). In practice, "recognizable" is whatever the Auditor accepts; looseness is encouraged for party play.

### 7.2 Chaining and the carried word

The first word of each new pair must match the carried word exactly. The destination is reached when an **accepted** pair's *second* word equals the destination word.

### 7.3 Rejection cap

Each turn allows a configurable number of rejected attempts (**default: 3**). Hitting the cap means:

- The player **forfeits the turn** (the partial attempt is discarded; the chain stays where it was).
- Play **passes to the next player or group**.

A lost turn is gentler and funnier than a hard scoring penalty, and it preserves round pace. The cap is host-configurable (including "off").

### 7.4 Loops

Loops are **allowed** (e.g. DOG HOUSE → HOUSE DOG → DOG HOUSE). They are not blocked because they're self-defeating: in Fewest Guesses they waste guesses, in Fastest Time they waste seconds. The software displays the full chain so loops are visible to everyone. *Optional toggle:* "no immediate repeat pair," for groups who want a little more rigor.

---

## 8. Player Structures & Digital Systems

### 8.1 Collective (co-op)

All players share **one chain** and take turns **round-robin**. The score is the team's. Win condition is cooperative: beat the team's own previous run, or beat a **par** the host sets by hand when choosing the start/destination. (Automatic par generation depends on a word graph and is a future feature, §11.)

### 8.2 Groups (competitive)

Players split into groups (minimum 2 per group). Each group builds its **own chain** from the same start/destination and is scored independently; best score wins, ties broken by the other metric (time breaks guess ties and vice versa).

Because v1 has only a human Auditor, groups can't truly play *simultaneously* on a single judge. Two workable approaches:

- **One Auditor per group** — clean and parallel, but it spends a judge per group, so it suits larger parties.
- **Staggered / batch auditing** — a single Auditor judges one group's submission at a time, or clears a batch between turns. Easier to staff, but slower.

True simultaneous racing on a shared judge would require an automated Auditor (§11).

### 8.3 What the software automates

| System | Responsibility |
|---|---|
| Chain tracker | Stores and displays the full accepted chain and current carried word |
| Turn manager | Round-robin order, Auditor rotation, group turn handoff |
| Timer | Per-turn clock; auto-pauses during auditing; aggregates total time |
| Guess counter | Counts accepted pairs; ignores rejections |
| Rejection counter | Enforces the per-turn cap and triggers turn forfeiture |
| Loop detector | Flags repeats for display (does not block, unless toggle is on) |

### 8.4 Start/destination word source (v1)

Because the human Auditor handles all validation, v1 needs **no word-pair dataset** — only a supply of start/destination pairs to seed rounds. This comes from a small **hand-curated list** (a few hundred entries is plenty), optionally themed, with the start/destination distance chosen by hand to keep rounds interesting. The host can also type in a custom start/destination on the spot.

A larger ambition — modeling the language as a **graph** (words = nodes, valid pairs = edges) to power automatic validation, guaranteed-solvable generated puzzles, automatic par scores, and hints — is deferred to §11, because it depends on the same unbounded word-pair enumeration that rules out an automated Auditor in v1.

---

## 9. Screens & UX

### 9.1 Player device

- Current **start → destination** banner and the **carried word** they must build from.
- A single text input + submit; clear pending/approved/rejected states.
- Their turn indicator and personal/group score.
- Reaction buttons (emoji) for non-active players to heckle/cheer.

### 9.2 Auditor device

- The submission, large, with **Approve / Reject** buttons.
- A required short **reason** field on reject (quick presets + free text).
- Persona indicator; rotation reminder.

### 9.3 Stage screen (shared)

- The growing **chain** rendered as connected links (the "linked list" visual).
- Live **timer** and/or **guess count** for the active mode.
- Group standings (in competitive mode).
- The Auditor's last rejection reason, for the table to react to.

---

## 10. Match Flow

1. **Lobby.** Host creates a room; players join by code and set names.
2. **Setup.** Host picks scoring mode (§5), player structure (§8), and rejection cap, and assigns the first Auditor. Start/destination is drawn from the curated list or hand-picked (§8.4).
3. **Round.** Core loop (§4) runs to the destination or a time/round limit.
4. **Scoreboard.** Result shown; chain replayed on the Stage.
5. **Rotate & repeat.** Auditor rotates; new round or end match.
6. **Results.** Match summary, best chains, fun superlatives (e.g. "Most Rejected," "Speed Demon").

---

## 11. Future / Stretch

- **Automated Auditor.** An optional non-human judge, unlocking solo play and true simultaneous group racing. The viable path is *not* a built-in dictionary of every pair (an unbounded, ever-changing set that would feel broken) but a **judge model** queried per submission — no dataset to maintain, handles slang and coinages, and can return a one-line reason for banter. Tradeoffs: latency, per-call cost, and non-determinism (fine for casual play, dicier for competitive scoring). This is the prerequisite for several items below.
- **Daily puzzle.** One shared start/destination per day, Fewest Guesses, with spoiler-free shareable results (Wordle-style social hook). Requires the automated Auditor.
- **Word graph & generated puzzles.** Model words as nodes and valid pairs as edges to enable guaranteed-solvable start/destination generation, automatic par scores, difficulty tiers (by shortest-path length and word commonness), and optional hints. Depends on resolving the same enumeration problem as the automated Auditor.
- **Custom word/theme packs** (food, sports, slang) and house-rule presets.
- **Spectator mode** for streamers, with audience reactions.
- **Persistent profiles & stats** (win rate, average guesses, fastest solve).

---

## 12. Open Questions

1. **Reason presets.** What's the funniest, fastest set of one-tap rejection reasons that keeps banter flowing without slowing the round?
2. **Competitive group staffing.** For group play, is one-Auditor-per-group or staggered/batch auditing the better default — and at what party size does each start to feel right?
3. **Destination feel.** Should reaching the destination one move early (a lucky direct pair) feel like a win or an anticlimax — and do we want to curate start/destination distance to avoid trivial rounds?
4. **Is an automated Auditor ever worth it?** A judge model would unlock solo and simultaneous play, but at the cost of the human subjectivity that makes the game funny. Does that tradeoff pay off, or is the human the whole point?

---

*Document owner: TBD · Next step: prototype the core loop (§4) with the Human Auditor and a hardcoded start/destination to playtest pacing and the rejection cap.*
