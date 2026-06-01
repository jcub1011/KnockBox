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

---

## 3. The Card System

### 3.1 Modifier Cards (Engine)
Modifiers are persistent cards that stay in a player's "Engine Bay." They are processed in a **Strict Left-to-Right Pipeline**.

*   **Additive Cards (+):** Best placed on the left.
    *   *Example:* **The Anchor** (+12 flat bonus).
    *   *Example:* **Consonant Crunch** (+2 per consonant).
*   **Multiplicative Cards (×):** Best placed on the right.
    *   *Example:* **Vowel Surge** (2× if vowels > consonants).
    *   *Example:* **The Architect** (1.5× for 8+ letter words).

### 3.2 Action Cards (Tactical)
Single-use cards that provide immediate utility or disruption.
*   **The Pivot:** Ignore the required starting letter; play any word.
*   **Amnesty:** Use the Banned Letter this turn for full points.
*   **Time Thief:** Subtract 5 seconds from the next player's current shot clock.

---

## 4. The "Era" System (4-Round Cycle)

The game is divided into 4-round "Eras." At the end of every 4th round, the game enters an **Intermission Phase**.

### 4.1 Intermission Phase Steps
1.  **The Deal:** Every player receives a fresh hand of Modifier and Action cards.
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

*   **Time Thief targets any opponent** (and can shorten a clock that is already ticking), not
    strictly "the next player" as written in §3.2. *(Confirmed intentional.)*
*   **Era 1 is cardless.** Players start with an empty Engine Bay and empty hand; the first Deal
    happens at the first Intermission (after `EraInterval` rounds), not at game start.
*   **Starting `ModifierSlots = 3`.** The GDD only specifies that Expansion grants +1 slot per
    Intermission; the starting capacity of 3 is an implementation choice.
*   **Shot clock configurable 5–20 s** rather than the GDD's stated 10–15 s window — the wider
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