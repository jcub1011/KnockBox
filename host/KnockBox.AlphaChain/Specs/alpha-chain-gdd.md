# Alpha-Chain: Game Design Document

## 1. Overview
**Alpha-Chain** is a high-velocity, web-based word game that merges the linguistic challenge of "Shiritori" with the strategic depth of an engine-builder. Players compete to build the highest-scoring "Value Chain" by stringing words together while managing a board of modifiers that evolve every four rounds.

---

## 2. Core Gameplay Mechanics

### 2.1 The Chain Logic
*   **Succession:** Every word played must begin with the **last letter** of the previous word.
*   **The Shot Clock:** Players have a 10–15 second window (configurable) to submit a valid word.
*   **Dictionary Validation:** Words must exist in the integrated dictionary API.
*   **Uniqueness:** Duplicate words are forbidden for the duration of the entire match.

### 2.2 The "Zero-Point Tax" (Banned Letters)
*   If a player submits a word containing a **Banned Letter**, the word is accepted to keep the chain alive, but the total score for that turn is **0**. 
*   This acts as a tactical "pass" to reset the clock and shift a difficult starting letter to the next opponent.
*   If a player plays a word with a banned letter as the last letter, then the next player can play any word.
*   **Tax Collector payout:** A taxed word is not merely lost points — any opponent holding a **Tax Collector** modifier (§3.6) collects **half** of the score the word would otherwise have made. A banned-letter play can therefore actively feed your rivals.

---

## 3. The Card System

Every card in the game is a persistent **Modifier Card** slotted into the player's **Engine Bay**.
The previous hand-held "Reaction" tier is **abolished** — there is one unified card tier. The bay
starts at **3 Modifier Slots** and gains +1 each Intermission, so defensive, utility, offensive and
scoring cards all compete for the same scarce slots (the **Intermission Dilemma**).

Two architecture rules govern every card:
*   **Zero UI Disruption.** No card may alter, glitch, or blind an *opponent's* interface. Any UI or
    timing pain a card inflicts is **self-inflicted**, chosen willingly for a big payoff.
*   **Zero Manual Targeting.** There is no point-and-click targeting during live rounds. All
    offensive cards fire automatically from rule-driven linguistic or leaderboard triggers.

### 3.1 Core Scorers
The pipeline `Score = (L + ΣA) × ΠM` is a **Strict Left-to-Right** walk; place additives left and
multipliers right.
*   **Additive (+):** **The Anchor** (+12), **Consonant Crunch** (+2/consonant), **Brick Layer**
    (+1/letter at 6+ letters), **Letter Hoarder** (+1/distinct letter), **High Roller** (+20 on a
    Q/X/Z/J start).
*   **Multiplicative (×):** **Vowel Surge** (×2 if vowels > consonants), **The Architect** (×2 at 8+),
    **Sesquipedalian** (×3 at 10+), **Guttural Roar** (×1.5 when the only vowels are A/E),
    **Perfect Link** (×1.5 ending in a vowel), **Sprinter** (≤4 letters → ×(1 + 0.1/second left)).

### 3.2 Glass Cannon & Chain Gambler (high-risk multipliers)
Big multipliers paid for in your **own** clock, UI, or rules. Clock effects apply when the owner's
turn arms (fraction first, then flat seconds), floored at a 3s minimum.
*   **Redline** — ×1.5 always; permanently −10% shot clock.
*   **The Blindfold** — ×1.8 always; hides **your own** input box while you type (no peeking at typos).
*   **Adrenaline Spike** — −4s shot clock; ×2.5 **only** if you submit in the final 2 seconds, else the
    word scores **0**.
*   **Panic Button** — −50% shot clock; ×1.35 normally, ×2.7 in the final 2 seconds.
*   **The Double Down** — ×2 if your word has a double letter (the 'ff' in *coffin*), else ×0.5.
*   **The Anchor Chain** — pins your clock to a strict, **unmodifiable 5 s** for the era; in exchange,
    ×(0.5 per letter) of the played word.
*   **The Vault** — ×1.5 always; permanently −3s shot clock.
*   **Hyper-Drive** — submit in under 3s to latch: clock drops to 5s for the era, **every multiplier
    doubled**. Inert in the pipeline; its power is the latch.
*   **Tunnel Vision** — ×2 always; your UI masks the first & last letter of the most recent chain word.

### 3.3 The Smuggler (random personal bans)
Each era these roll the owner a random **personal banned letter** (drawn from the legal pool, dodging
the era letter) that triggers the Zero-Point Tax for the owner alone.
*   **The Roulette Wheel** — ×1.75 on every word you keep clean of both bans.
*   **The Toll Booth** — bank **20%** of any opponent's score when their word uses your personal letter
    (they keep their points; you are minted the cut).

### 3.4 Automated Aggro (zero-friction PvP)
Hands-free offensive cards that fire from the leaderboard or linguistic patterns — never targeted.
*   **Flak Cannon** — +5 flat; at the end of your turn, shaves ~2s off the next clock of **every player
    scoring higher than you**.
*   **Scattershot** — ×1.15; on every submission, shaves ~3s off any opponent who has played a
    double-letter word this era.
*   **The Bounty Hunter** — 0 points; marks the round's leader — if they play a word shorter than 6
    letters, they are docked **−30 points**.
*   **Tracer Round** — 0 points; at the end of your turn, the letter your word **ends on** becomes a
    one-turn personal banned letter for the next player.

### 3.5 The Shield
*   **The Titanium Mirror** — starts the era as a passive ×1.0. Automatically blocks and **reflects**
    incoming automated attacks (time shaves, point drains, letter hijacks) back at their source, but
    its multiplier permanently drops **−0.1× per block** (1.0 → 0.9 → 0.8 …), decaying into a scoring
    **burden** you carry until the next Intermission.

### 3.6 Tax Economy
These resolve reactively against *opponents'* submissions (see §2.2); they do not fold into the
owner's own scoring pipeline.
*   **Tax Collector** — when an opponent eats the Zero-Point Tax, collect **half** the would-be score.
*   **The IRS Agent** — 0 points; when *your own* word is taxed, no Tax Collector profits from it.
*   **Bait & Switch** — when your word is taxed, force the offending banned letter onto the **next
    player** as a personal ban for their turn.

### 3.7 Utility (counter-balances, 0 points)
Lifesavers that occupy a scoring slot but pair with the high-risk cards above.
*   **The Heat Sink** — +5s shot clock (neutralises Redline / Adrenaline Spike — but not the Anchor
    Chain's unmodifiable clock).
*   **The Faraday Cage** — immune to personal banned letters generated by **your own** cards (Roulette
    Wheel, The Toll Booth) — keep the boosts without the vocabulary tax.
*   **The Prism** — if your word is a typo or fails validation, your shot clock resets to full (once
    per turn) instead of ending the turn. The Blindfold's essential pairing.
*   **The Wildcard** — your words may ignore the Succession rule (need not begin with the previous
    word's last letter).
*   **The Catalyst** — the letters Y, W and H count as **both** vowel and consonant when evaluating
    every other card's trigger.

---

## 4. The "Era" System (4-Round Cycle)

The game is divided into 4-round "Eras." At the end of every 4th round, the game enters an **Intermission Phase**.

### 4.1 Intermission Phase Steps
1.  **The Deal:** Every player receives fresh Modifier cards (§3) appended to their Engine Bay.
2.  **Expansion:** Every player gains one additional **Modifier Slot**.
3.  **Optimization (Fog of War):** Players rearrange their current and new modifiers. Opponents' cards are hidden during this step.
4.  **The Sniper Ban:** The player in **last place** selects any letter of the alphabet to be the **Banned Letter** for the next 4 rounds.

---

## 5. Mathematical Scoring Logic

The score for any given word is calculated by piping the base word length through the modifier chain from index `0` to `n`.

### 5.1 The Formula
Let $L$ be the word length, $A$ be the sum of active additives, and $M$ be the product of active multipliers.

$$Score = (L + \sum A_{i}) \times \prod M_{j}$$

*Note: Conditional modifiers only contribute to the sum/product if their specific criteria (e.g., "Contains 'X'") are met.*

### 5.2 Time-aware and meta factors
Some multiplicative cards read **turn context** rather than just the word: Sprinter and Panic Button
scale with the seconds left on the shot clock at submit time. A latched **Hyper-Drive** raises a
global **multiplier scale** for the rest of the era, so every multiplicative card's factor `M` is
applied as `M × scale` (scale = 2 under Hyper-Drive) — "all multipliers doubled" without touching any
individual card. The result is still rounded half-up and clamped to the max word score.

---

## 6. Win Conditions
*   **Highest Score:** The player with the highest cumulative points at the end of the final Era wins.
*   **Survival (Optional):** If a player fails to enter a word before the shot clock hits zero, they are eliminated (or penalized significantly).

---

## 7. Host Configuration
*   **Ban Selection:** Ban Vowels Only | Ban Consonants Only | All Bannable
*   **Timer:** A configurable timer for time to enter words, time to select cards during card distribution stage, and time to select a letter to ban.
*   **Era Interval:** Allow changing the interval of eras from 4 rounds to any number of rounds greater than 0.
*   **Era Count:** Set the number of eras that must pass before the game ends. Default to 4 eras.

---

## 8. Implementation Deviations

The shipped implementation makes the following confirmed, intentional departures from the
design above. They are recorded here so the GDD stays the single source of truth for *what the
game actually does*.

*   **One unified card tier (no hand-played or reaction cards).** Both the original proactive action
    cards *and* the later auto-firing reaction hand are abolished — every card is now a persistent
    Engine Bay **Modifier** (§3). Offensive behaviour that used to live in the reaction hand is
    re-homed as automated modifiers that fire from leaderboard/linguistic rules with no targeting and
    no opponent-UI disruption: the old Toll Booth reaction became the engine **Toll Booth** (§3.3),
    Riposte became **The Titanium Mirror** (§3.5), and Jinx/Frostbite/Censor are superseded by
    **Tracer Round**, **Flak Cannon** and the Sniper Ban. Amnesty/Free Throw/Overtime/Windfall/
    Feedback Loop are dropped (their roles are covered by utility modifiers like the Faraday Cage,
    Wildcard, Heat Sink and Prism). An "engine effect" overlay tells a targeted player what hit them
    and why. *(Confirmed intentional.)*
*   **Era 1 is cardless.** Players start with an empty Engine Bay; the first Deal happens at the first
    Intermission (after `EraInterval` rounds), not at game start.
*   **Starting `ModifierSlots = 3`.** The GDD only specifies that Expansion grants +1 slot per
    Intermission; the starting capacity of 3 is an implementation choice.
*   **Shot clock configurable 5–60 s** rather than the GDD's stated 10–15 s window — the wider
    range gives hosts more room for very fast or very relaxed matches.
*   **"Fresh deal" = append, not replace.** Dealt modifiers accumulate on the existing Engine Bay;
    a Deal never clears what a player already holds.
*   **Distinct modifiers.** Dealt modifiers are always distinct from the cards a player already
    holds (the Engine Bay is keyed by card id for reordering), which caps a player's lifetime
    modifiers at the catalogue size.
*   **Host-plays / two start buttons.** The host may start as a shared display (not a player) or
    as a player; this is chosen at start time by the two start buttons and is not described in
    the GDD. The choice is never persisted to the host's saved settings.
*   **Sniper-ban timeout fallback.** If the last-place picker never chooses (or leaves while
    holding the ban), the SniperBan sub-phase times out and a legal banned letter is drawn at
    random so the match never stalls.
*   **Over-capacity discard rule.** When a Deal overflows a player's slot count, a submitter
    discards explicitly during Optimization; a non-submitter keeps their current order and the
    **oldest** cards (left side) are dropped to fit the expanded capacity.
*   **Optional score cap.** A single word's score is clamped to `ScoreCalculator.MaxWordScore`
    (10,000) so a stack of multiplicative conditionals can't blow out the UI.
*   **Home-page tile.** The final tile art is supplied via the plugin manifest's `tileAsset`
    (`wwwroot/tile.svg`) — the platform's home page renders manifest tiles directly — rather
    than through a bespoke `AlphaChainTile` Razor component, which the host could not reference
    across the plugin boundary.
*   **Engine Bay reorder is Intermission-only.** Reorder is disabled during a live round (the
    scoring pipeline order is frozen) and performed at the Intermission's Optimization sub-phase.
    Reorder is HTML5 drag-and-drop with a keyboard fallback (focus a card, then ←/→ to move it).
*   **Card capabilities are declarative descriptors.** Beyond the pure `Trigger`/`Value` scoring
    delegates, a `ModifierCard` may declare optional, data-only descriptors (clock effect, siphon
    rule, era-start personal ban, own-tax override, forced next-player ban, Hyper-Drive latch, UI
    mask) resolved centrally by the engine at fixed lifecycle hooks (clock arming, era start,
    submission). Adding an exotic card is "fill in the descriptor(s)"; only a new *kind* of hook
    touches the engine. The scoring delegates stay pure (they never reach into room state).
*   **Per-owner shot-clock effects.** The glass-cannon cards (Vault, Redline, Panic Button) and a
    latched Hyper-Drive change the *owner's* armed shot clock — fractions applied before flat
    seconds, floored at a 3s minimum — so the clock can shorten but never zero or invert.
*   **Generalised siphons.** The Tax Collector bounty is one case of a general "siphon": a player's
    matching collectors take the single highest rate (no stacking), and The Toll Booth siphons on a
    *normally-scored* word that used the owner's era-rolled personal letter (minting the owner a cut
    without docking the submitter). The IRS Agent can suppress the taxed bounty entirely.
*   **Automated attacks route through the Titanium Mirror.** The offensive modifiers (Flak Cannon and
    Scattershot time-shaves, the Bounty Hunter point-drain, Tracer Round and Bait & Switch letter
    hijacks) all resolve through a single helper that lets the victim's Titanium Mirror block and
    reflect the hit back at its caster (decaying its multiplier per block). The pass is single-shot —
    a reflected hit always lands on the caster and is never itself re-reflected.
*   **Deterministic submit time.** The submission timestamp is threaded into the FSM (not read from
    the wall clock mid-pipeline) so time-aware scoring (Sprinter, Panic Button, Adrenaline Spike) and
    the Hyper-Drive elapsed check are reproducible under test.
*   **The Blindfold is client-enforced.** While the local player holds The Blindfold, a CSS class
    hides their own input text (the caret stays visible). The input still works and server validation
    is unchanged — only the rendered glyphs are hidden, so the penalty is purely self-inflicted.