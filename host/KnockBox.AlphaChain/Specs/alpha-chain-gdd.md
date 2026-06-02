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
*   **Tax Collector payout:** A taxed word is not merely lost points — any opponent holding a **Tax Collector** modifier (§3.1) collects **half** of the score the word would otherwise have made. A banned-letter play can therefore actively feed your rivals.

---

## 3. The Card System

### 3.1 Modifier Cards (Engine)
Modifiers are persistent cards that stay in a player's "Engine Bay." They are processed in a **Strict Left-to-Right Pipeline**.

*   **Additive Cards (+):** Best placed on the left.
    *   **The Anchor** (+12 flat bonus).
    *   **Consonant Crunch** (+2 per consonant).
    *   **Brick Layer** (+1 per letter when the word is 6+ letters).
    *   **Letter Hoarder** (+1 per distinct letter).
    *   **High Roller** (+20 when the word begins with a rare tile — Q, X, Z or J).
*   **Multiplicative Cards (×):** Best placed on the right.
    *   **Vowel Surge** (2× if vowels > consonants).
    *   **The Architect** (**2×** for 8+ letter words).
    *   **Sesquipedalian** (3× for 10+ letter words — clamped to the max score, a guaranteed payout).
    *   **Guttural Roar** (1.5× when the word's only vowels are A/E — the multiplicative counterpart to Vowel Surge).
    *   **Perfect Link** (1.5× when the word ends in a vowel — sets the next player up, pads your score).
    *   **Sprinter** (when the word is ≤4 letters, ×(1 + 0.1 per second left on your clock) — blitz short words for a soaring multiplier).

**Glass-cannon clock cards.** Big multipliers paid for in shot-clock time. Each applies its clock
effect (fraction first, then flat seconds) when its owner's turn arms, floored at a 3s minimum.
*   **The Vault** (1.5× always, but permanently −3s shot clock).
*   **Redline** (1.5× always, but permanently −10% shot clock).
*   **Panic Button** (−50% shot clock; ×1.35 normally, ×2.7 if you submit in the final 2 seconds).
*   **Hyper-Drive** (submit in under 3 seconds to latch: your clock drops to 5s for the rest of the
    era, but **every multiplier you own is doubled**). Inert in the pipeline; its power is the latch.
*   **Tunnel Vision** (2× always, but your UI masks the first & last letter of the most recent chain
    word — you rely on memory or the required-start letter; the chain rule is still enforced).

**Reactive-bounty / tax-economy cards.** These do **not** fold into their owner's own scoring
pipeline; they resolve reactively against *opponents'* submissions (see §2.2).
*   **Tax Collector** — when an opponent eats the Zero-Point Tax, collect **half** the points the
    word would have scored.
*   **Enforcer** — a stronger Tax Collector: collect **75%**. A player's matching collectors take the
    single highest rate (Tax Collector + Enforcer = 75%, **not** 125%).
*   **IRS** — when *your own* word is hit by the Zero-Point Tax, score a flat **+15** instead of 0, and
    no Tax Collector profits from it.
*   **Bait & Switch** — when your word is taxed, force the offending banned letter onto the **next
    player** as a personal ban for their turn.

**Personal-ban cards.** Each era these roll the owner a random **personal banned letter** (drawn from
the match's legal pool, dodging the era letter) that triggers the Zero-Point Tax for the owner alone.
*   **The Roulette Wheel** — reward: ×1.75 on every word you keep clean (taxed words score 0 anyway).
*   **Smuggler's Toll** — reward: bank **20%** of any opponent's score when their word uses your
    personal letter (they keep their points; you are minted the cut).

### 3.2 Reaction Cards (Tactical)
Single-use cards that sit passively in a player's hand and **auto-fire on game events** — only
when they would actually help (never wasted), with no manual play and no targeting. They replace
the original hand-played "action cards", whose mid-turn sifting was dead weight under the shot
clock. At most one matching reaction per holder fires per event (oldest first).

**Defensive** (fire on something that happens to you):
*   **Amnesty:** When you play a banned-letter word, the Zero-Point Tax is suppressed and the word scores in full.
*   **Free Throw:** When your turn opens on a rare required letter (Q/X/Z/J/K/V), the requirement is cleared. *(Reworked from the original "Pivot".)*
*   **Overtime:** When your shot clock runs out, gain a few seconds and keep your turn — once (saves you from a 0-score timeout, or elimination in Survival).
*   **Windfall:** When you fall to last place, immediately draw 2 more reaction cards — **at most once per era**, so a player riding the 3rd/4th boundary can't loop-draw their deck.

**Offensive** (fire on an opponent's action; routed through Riposte):
*   **Toll Booth:** When an opponent *ahead of you* posts a 7+ letter word, **steal 20% of the points they just earned** (it hurts them and helps you, without locking them out of playing).
*   **Frostbite:** When an opponent overtakes you specifically, shave ~5 s off their next shot clock.
*   **Jinx:** When an opponent takes the overall lead, curse their next word with a personal banned letter.

**Special:**
*   **Censor:** When you fall to last place, ban an extra letter for everyone for one round (Riposte holders are spared).
*   **Riposte:** When an attack reaction targets you, negate it and reflect it back at the caster; against board-wide effects (Censor), its holder is simply exempt.
*   **Feedback Loop:** When your Riposte negates an attacker, **silence** them — their word input is locked for the first 3 seconds of their next turn (the shot clock keeps running).

---

## 4. The "Era" System (4-Round Cycle)

The game is divided into 4-round "Eras." At the end of every 4th round, the game enters an **Intermission Phase**.

### 4.1 Intermission Phase Steps
1.  **The Deal:** Every player receives a fresh hand of Modifier and Reaction cards (§3.1–§3.2).
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

*   **Tactical cards are auto-firing reactions, not hand-played actions.** The original §3.2
    action cards (Pivot, Amnesty, Time Thief) were proactive — you had to find and play the right
    card mid-turn, which was friction under the shot clock. They are replaced by the reaction system
    described in §3.2: cards sit in hand and auto-fire on game events, only when beneficial. Amnesty
    is preserved (now auto-firing), Pivot became **Free Throw** (rare-letter rescue at turn start),
    Time Thief is removed, and seven new cards were added (Overtime, Windfall, Toll Booth, Frostbite,
    Jinx, Censor, Riposte). A "reaction strike" overlay tells a targeted player what hit them and why.
    *(Confirmed intentional.)*
*   **Era 1 is cardless.** Players start with an empty Engine Bay and empty hand; the first Deal
    happens at the first Intermission (after `EraInterval` rounds), not at game start.
*   **Starting `ModifierSlots = 3`.** The GDD only specifies that Expansion grants +1 slot per
    Intermission; the starting capacity of 3 is an implementation choice.
*   **Shot clock configurable 5–60 s** rather than the GDD's stated 10–15 s window — the wider
    range gives hosts more room for very fast or very relaxed matches.
*   **"Fresh hand" = append, not replace.** Dealt modifiers/actions accumulate on the existing
    bay/hand; a Deal never clears what a player already holds.
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
    matching collectors take the single highest rate (no stacking), and Smuggler's Toll siphons on a
    *normally-scored* word that used the owner's era-rolled personal letter (minting the owner a cut
    without docking the submitter). IRS can suppress the taxed bounty entirely.
*   **Deterministic submit time.** The submission timestamp is threaded into the FSM (not read from
    the wall clock mid-pipeline) so time-aware scoring (Sprinter, Panic Button) and the Hyper-Drive
    elapsed check are reproducible under test.
*   **Feedback Loop silence is client-enforced.** A queued silence locks the word input (readOnly +
    a key-swallowing flag in `alpha-chain-input.js`) for its first seconds; the shot clock keeps
    running, so the silence is a real tempo penalty. The lock is owner-only and presentational —
    server validation is unchanged.