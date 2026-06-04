# Alpha-Chain: Game Design Document

## 1. Overview
**Alpha-Chain** is a high-velocity, web-based word game that merges the linguistic challenge of "Shiritori" with the strategic depth of an engine-builder. Players compete to build the highest-scoring "Value Chain" by stringing words together while managing a board of modifiers that evolve every four rounds.

---

## 2. Core Gameplay Mechanics

### 2.1 The Chain Logic
*   **Succession:** Every word played must begin with the **last letter** of the previous word.
*   **The Shot Clock:** Players have a configurable **5–60 second** window to submit a valid word.
*   **Dictionary Validation:** Words must exist in the integrated dictionary API.
*   **Uniqueness:** Duplicate words are forbidden for the duration of the entire match.

### 2.2 The "Zero-Point Tax" (Banned Letters)
*   If a player submits a word containing a **Banned Letter**, the word is accepted to keep the chain alive, but the total score for that turn is **0**. 
*   This acts as a tactical "pass" to reset the clock and shift a difficult starting letter to the next opponent.
*   If a player plays a word with a banned letter as the last letter, then the next player can play any word.
*   **Tax Collector payout:** A taxed word is not merely lost points — any opponent holding a **Tax Collector** modifier (§3.5) collects **half** of the score the word would otherwise have made. A banned-letter play can therefore actively feed your rivals.

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

The catalogue below is the **complete set of 32 shipped modifier cards**. Each is tagged
Additive (+), Multiplier (×), or **FX** — a scoring-inert card (base ×1.0) whose power is a side
effect. Clock effects apply when the owner's turn arms (fractions summed first, then flat seconds),
floored at a **3 s** minimum.

### 3.1 Core Additives (+)
Flat or per-letter point bumps; place these **left** so multipliers act on a bigger base.
*   **The Anchor** — +10 flat, always.
*   **Vanilla** — +1 per letter in the word.
*   **Consonant Crunch** — +2 per consonant.
*   **Vocal Vowels** — +3 per vowel.
*   **Brick Layer** — +1 per letter, but only when the word is **6+ letters**.
*   **Letter Hoarder** — +1 per **distinct** letter.
*   **High Roller** — +20 when the word **starts** with a rare letter (Q, X, Z, J).

### 3.2 Core Multipliers (×)
Conditional multipliers; place these **right** so they scale the accumulated additives.
*   **Vowel Surge** — ×3 when the word has **more vowels than consonants**.
*   **The Architect** — ×2 when the word is **8+ letters**.
*   **Sesquipedalian** — ×3 when the word is **10+ letters**.
*   **Guttural Roar** — ×1.5 when the word's **only** vowels are A or E.
*   **Perfect Link** — ×1.5 when the word **ends in a vowel** (and hands the next player an easy letter).
*   **Speedracer** — when the word is **longer than 4 letters**, ×(1 ÷ [remaining ÷ total clock]),
    **capped at ×2** — reward for submitting fast.
*   **The Double Down** — ×2 when the word has a **repeat letter** (the 'ff' in *coffin*), else ×0.5.

### 3.3 Glass Cannon (high-risk clock multipliers)
Big multipliers paid for in your **own** shot clock or UI.
*   **The Vault** — ×1.5 always; permanently **−10%** shot clock.
*   **Redline** — ×2 always; permanently **−20%** shot clock.
*   **Panic Button** — **−50%** shot clock; ×1.35 normally, **×2.7** if you submit before the final 2 seconds.
*   **The Anchor Chain** — pins your clock to a strict, **unmodifiable 5 s** for the era; in exchange,
    ×(0.5 per letter) of the played word.
*   **Hyper-Drive** — **FX**. Submit in under **3 s** elapsed to **latch** an overdrive for the rest of
    the era: your base clock drops to 5 s and **every multiplier you own is doubled**. Inert in the
    scoring fold itself — its power is the latch.
*   **The Blindfold** — ×1.8 always; hides **your own** input box while you type (no peeking at typos).

### 3.4 Personal-Ban Economy (random personal bans)
Each era these roll the owner a random **personal banned letter** (drawn from the legal pool, dodging
the era letter) that triggers the Zero-Point Tax for the owner alone.
*   **The Roulette Wheel** — ×1.75 on every word you keep clean of the ban.
*   **The Toll Booth** — **FX**. Banks **20%** of any opponent's earned score when their word uses your
    personal letter (they keep their points; you are minted the cut).

### 3.5 Tax Economy
These resolve reactively against *opponents'* taxed submissions (see §2.2); they do not fold into the
owner's own scoring pipeline.
*   **Tax Collector** — **FX**. When an opponent eats the Zero-Point Tax, collect **half** the would-be score.
*   **The IRS Agent** — **FX**. When *your own* word is taxed, you keep 0 and no opponent's Tax Collector profits from it.
*   **Bait & Switch** — **FX**. When your word is taxed, curse the **next player** with the offending
    banned letter as a personal ban for their next turn.

### 3.6 Automated Aggression (zero-friction PvP)
Hands-free offensive cards that fire from the leaderboard or linguistic patterns — never targeted.
*   **Flak Cannon** — **FX** (0 points). At the end of your turn, shaves **2 s** off the next clock of
    **every player scoring higher than you**.
*   **The Bounty Hunter** — **FX** (0 points). Marks the round's leader — if they play a word shorter
    than 6 letters, they are docked **−15 points**.

### 3.7 The Shield
*   **The Titanium Mirror** — a passive ×1.0 when fresh. Automatically blocks and **reflects**
    incoming automated attacks (time shaves, point drains, letter hijacks) back at their source, but
    its multiplier permanently drops **−0.1× per block** (1.0 → 0.9 → 0.8 …), decaying into a scoring
    **burden**. The decay carries **across eras** — it is *not* reset at the Intermission. The only
    way back to ×1.0 is to be dealt a **fresh mirror** (a replacement); a player holds at most one.

### 3.8 Utility (counter-balances, 0 points)
Lifesavers that occupy a scoring slot but pair with the high-risk cards above.
*   **The Heat Sink** — **FX**. +30% shot clock (neutralises Redline / Vault — but not the Anchor
    Chain's unmodifiable clock).
*   **The Prism** — **FX**. If your word is a typo or fails validation, your shot clock resets to full
    (once per turn) instead of ending the turn. The Blindfold's essential pairing.
*   **The Wildcard** — **FX**. Your words may ignore the Succession rule (need not begin with the
    previous word's last letter).
*   **The Catalyst** — **FX**. For every card placed **after** it in the bay, the letters Y, W and H
    count as a **vowel** in addition to their normal consonant role (i.e. both) when evaluating that
    card's trigger.

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

A word is scored by `EngineEvaluator.CalculateSteps`, which walks the player's Engine Bay **strictly
left → right** as a sequential fold. The running value is **seeded with the word length**, then each
triggered card folds itself in (`ExecuteModifier`): an additive card does `value + magnitude`, a
multiplicative card does `value × (factor × MultiplierScale)`. The final running total is rounded
**half-up** and clamped to `MaxWordScore` (**10,000**). The walk emits a `ScoreBreakdown` — a
per-card `ScoreStep` trace (operator, value, running score) — which the UI replays.

### 5.1 The Formula
Let $L$ be the word length, $A$ be the sum of active additives, and $M$ be the product of active multipliers.

$$Score = (L + \sum A_{i}) \times \prod M_{j}$$

This is a convenient summary of the fold, **not** the exact algorithm: because the walk is sequential
left → right, **placement order matters** — a multiplier placed before an additive multiplies a
smaller base. Conditional cards only contribute when their trigger fires for the word.

### 5.2 Time-aware and meta factors
Some multiplicative cards read **turn context** rather than just the word: Speedracer and Panic Button
scale with the seconds left on the shot clock at submit time. A latched **Hyper-Drive** raises a
global **multiplier scale** for the rest of the era (seeded once from the bay's
`IMultiplierScaleProvider` cards), so every multiplicative card's factor `M` is applied as
`M × scale` (scale = 2 under Hyper-Drive) — "all multipliers doubled" without touching any individual
card.

### 5.3 Evaluation architecture
Behavior beyond the pure scoring fold is expressed three ways, so adding an exotic card never touches
the evaluator's core loop:

*   **Capability interfaces, discovered by walking the bay.** A card opts into an interface and the
    engine finds it generically: letter classification (`IConsonantChecker` / `IVowelChecker` — the
    Catalyst), shot-clock override / base / modifier (`IShotClockOverride` / `IBaseShotClockProvider`
    / `IShotClockModifier` — Anchor Chain, Hyper-Drive, Vault, Redline, Panic Button, Heat Sink),
    multiplier scale (`IMultiplierScaleProvider` — Hyper-Drive), own-tax policy (`IOwnTaxPolicy` — IRS
    Agent), succession exemption (`ISuccessionExemption` — Wildcard), attack interception
    (`IAttackInterceptor` — Titanium Mirror), and input masking (`IInputMask` — Blindfold). Letter
    walks honor the **last** provider before the current card; engine-policy walks scan the whole bay.
*   **Lifecycle hooks** fired at fixed points: `OnEraStart` (roll personal bans), `OnWordAccepted`
    (Hyper-Drive latch), `OnTurnEnded` (Flak Cannon, Bait & Switch), `OnOpponentWordResolved` (Tax
    Collector, Toll Booth, Bounty Hunter), `OnValidationFailed` (Prism clock refill).
*   **Card-contributed, player-keyed room-state services** (`IContributesRoomServices`): the shield,
    Hyper-Drive latch, personal-ban, time-penalty, letter-hijack, and Prism-guard state each live in
    their own service, instantiated as the union across the whole catalogue and reset on the right
    turn / era boundary — never in the FSM.

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
    no opponent-UI disruption: the old Toll Booth reaction became the engine **Toll Booth** (§3.4),
    Riposte became **The Titanium Mirror** (§3.7), and Jinx/Frostbite/Censor are superseded by
    **Flak Cannon**, **Bait & Switch** and the Sniper Ban. Amnesty/Free Throw/Overtime/Windfall/
    Feedback Loop are dropped (their roles are covered by utility modifiers like the Wildcard, Heat
    Sink and Prism). An "engine effect" overlay tells a targeted player what hit them and why.
    *(Confirmed intentional.)*
*   **Era 1 is cardless.** Players start with an empty Engine Bay; the first Deal happens at the first
    Intermission (after `EraInterval` rounds), not at game start.
*   **Starting `ModifierSlots = 3`.** The GDD only specifies that Expansion grants +1 slot per
    Intermission; the starting capacity of 3 is an implementation choice.
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
*   **Card behaviour is interfaces + hooks + room services, not data descriptors.** Each card is a
    self-contained `IModifierCard`: it folds into scoring via `ExecuteModifier` and expresses
    everything beyond pure scoring by (a) opting into **capability interfaces** the engine discovers
    by walking the bay (letter classification, shot-clock override/base/modifier, multiplier scale,
    own-tax policy, succession exemption, attack interception, input mask), (b) overriding
    **lifecycle hooks** (`OnEraStart`, `OnWordAccepted`, `OnTurnEnded`, `OnOpponentWordResolved`,
    `OnValidationFailed`), and (c) declaring **card-contributed, player-keyed room-state services**
    via `IContributesRoomServices` (see §5.3). Adding an exotic card is "implement the interface(s)
    and hook(s)"; only a genuinely new *kind* of capability touches the engine. Scoring stays pure —
    side-effecting state lives in the room services, never in the scoring fold.
*   **Per-owner shot-clock effects.** The glass-cannon cards (Vault, Redline, Panic Button) and a
    latched Hyper-Drive change the *owner's* armed shot clock — fractions applied before flat
    seconds, floored at a 3s minimum — so the clock can shorten but never zero or invert.
*   **Generalised siphons.** The Tax Collector bounty is one case of a general "siphon": a player's
    matching collectors take the single highest rate (no stacking), and The Toll Booth siphons on a
    *normally-scored* word that used the owner's era-rolled personal letter (minting the owner a cut
    without docking the submitter). The IRS Agent can suppress the taxed bounty entirely.
*   **Automated attacks route through the Titanium Mirror.** The offensive modifiers (the Flak Cannon
    time-shave, the Bounty Hunter point-drain, and the Bait & Switch letter hijack) all resolve
    through a single helper that lets the victim's Titanium Mirror block and reflect the hit back at
    its caster (decaying its multiplier per block). The pass is single-shot — a reflected hit always
    lands on the caster and is never itself re-reflected.
*   **Deterministic submit time.** The submission timestamp is threaded into the FSM (not read from
    the wall clock mid-pipeline) so time-aware scoring (Speedracer, Panic Button) and the Hyper-Drive
    elapsed check are reproducible under test.
*   **The Blindfold is client-enforced.** While the local player holds The Blindfold, a CSS class
    hides their own input text (the caret stays visible). The input still works and server validation
    is unchanged — only the rendered glyphs are hidden, so the penalty is purely self-inflicted.