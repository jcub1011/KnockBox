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
*   **Multiplicative Cards (×):** Best placed on the right.
    *   **Vowel Surge** (2× if vowels > consonants).
    *   **The Architect** (1.5× for 8+ letter words).
    *   **Sprinter** (1.25× when the word is 4 letters or shorter).

**Tax Collector (reactive bounty).** A special modifier that does **not** fold into its owner's own
scoring pipeline. Instead it pays out reactively: whenever an *opponent* plays a banned-letter word
and eats the Zero-Point Tax (§2.2), each Tax Collector owner collects **half** the points that word
would have scored. It is the engine-side counterpart to the Zero-Point Tax — a banned-letter play
that scores 0 for the speaker can still pay out to everyone holding this card.

### 3.2 Reaction Cards (Tactical)
Single-use cards that sit passively in a player's hand and **auto-fire on game events** — only
when they would actually help (never wasted), with no manual play and no targeting. They replace
the original hand-played "action cards", whose mid-turn sifting was dead weight under the shot
clock. At most one matching reaction per holder fires per event (oldest first).

**Defensive** (fire on something that happens to you):
*   **Amnesty:** When you play a banned-letter word, the Zero-Point Tax is suppressed and the word scores in full.
*   **Free Throw:** When your turn opens on a rare required letter (Q/X/Z/J/K/V), the requirement is cleared. *(Reworked from the original "Pivot".)*
*   **Overtime:** When your shot clock runs out, gain a few seconds and keep your turn — once (saves you from a 0-score timeout, or elimination in Survival).
*   **Windfall:** When you fall to last place, immediately draw 2 more reaction cards.

**Offensive** (fire on an opponent's action; routed through Riposte):
*   **Toll Booth:** When an opponent *ahead of you* posts a big word, shave ~5 s off their next shot clock.
*   **Frostbite:** When an opponent overtakes you specifically, shave time off their next shot clock.
*   **Jinx:** When an opponent takes the overall lead, curse their next word with a personal banned letter.

**Special:**
*   **Censor:** When you fall to last place, ban an extra letter for everyone for one round (Riposte holders are spared).
*   **Riposte:** When an attack reaction targets you, negate it and reflect it back at the caster; against board-wide effects (Censor), its holder is simply exempt.

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