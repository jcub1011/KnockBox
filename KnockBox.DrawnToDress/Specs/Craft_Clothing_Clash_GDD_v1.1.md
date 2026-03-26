# Craft Clothing Clash
## Game Design Document

**Version:** 1.1  
**Date:** March 2026  
**Status:** Design Complete - Ready for Implementation

---

## Table of Contents

1. [High Concept](#high-concept)
2. [Game Overview](#game-overview)
3. [Core Mechanics](#core-mechanics)
4. [Game Flow](#game-flow)
5. [Rules & Constraints](#rules--constraints)
6. [Scoring System](#scoring-system)
7. [Configurable Settings](#configurable-settings)
8. [UI/UX Requirements](#uiux-requirements)
9. [Terminology](#terminology)
10. [Edge Cases & Resolution](#edge-cases--resolution)

---

## High Concept

**Craft Clothing Clash** is a competitive party game blending artistic creation, strategic resource management, and social voting. Players draw clothing items, race to build themed outfits from a shared pool, and compete through peer voting. Success requires balancing drawing ability, decision-making speed, thematic understanding, and taste.

**Target Audience:** Family and friend groups, 6+ players  
**Game Length:** 20–45 minutes (depends on settings)  
**Platform:** Digital (web - Blazor)

---

## Game Overview

### Win Condition
The player with the highest total points after all voting rounds wins. If two or more players tie on total points, the tiebreaker is the player who won the most individual matchups. If that is also tied, the winner is determined by a coin flip.

### Core Loop
1. **Draw** articles of clothing under time pressure
2. **Reveal** the full clothing pool to all players before picking begins
3. **Pick** items from a shared pool in simultaneous real-time races
4. **Build** two themed outfits with chosen items
5. **Vote** on outfits based on theme adherence and personal preference
6. **Score** based on voting results and tournament advancement

### Why It Works
- **Multiple skill types valued:** Drawing, strategy, taste, speed
- **Shared economy creates tension:** Your best drawing might be stolen
- **Theming elevates humor:** Random combinations become narratively coherent
- **Peer voting is democratic:** No algorithm, just human judgment
- **Swiss voting scales:** Works for 6 players or 20+

---

## Core Mechanics

### 1. Drawing Phase
Players create clothing items to populate the shared pool.

**What happens:**
- Game runs sequential drawing rounds (hat → shirt → pants → shoes)
- Each round lasts a fixed duration (default: 60 seconds)
- Players draw as many items as they can, up to the configured limit
- All drawings are stored and added to a shared pool

**Key constraints:**
- Maximum items per clothing type per player (default: 5)
- Players cannot see other players' drawings during this phase
- Drawings are digital sketches (stylus/mouse input)
- Each drawing is attributed to its creator (used to block self-picks later)

**Win condition:** None—purely generative. Quality doesn't matter yet.

---

### 2. Pool Reveal

After the drawing phase and before outfit building begins, all drawings in the shared pool are revealed to every player simultaneously.

**What happens:**
- Game displays an animated reveal of all collected drawings, grouped by clothing type
- Players can browse the full pool at their leisure
- Each player must press a **"Ready"** button to confirm they have seen the pool
- Once all players are ready (or a short countdown timer expires), the outfit building phase begins

**Purpose:**
- Creates a natural social moment — players react to each other's drawings before competing for them
- Ensures no player is surprised mid-pick by items they haven't seen
- Builds anticipation before the race begins

**Key constraints:**
- Players cannot claim items during the reveal — it is view-only
- A countdown timer (default: 30 seconds) auto-advances if not all players press Ready

**Outcome:** All players have seen the full pool; outfit building begins with full information

---

### 3. Outfit Building Phase (Simultaneous Real-Time Picking)

Players select items from a shared pool to create outfits. **Picks are simultaneous and happen in real-time.**

**What happens:**
- Host triggers outfit building phase
- Shared clothing pool is displayed with all available items
- Players are presented with a clothing grid (hat, shirt, pants, shoes slots)
- Each player clicks/drags items into their outfit slots
- **First player to claim an item owns it** (item removed from pool)
- If a player tries to pick an item another player just claimed, selection fails (they must pick something else)
- Player can change their picks until time runs out
- Once a player is satisfied, they lock in their outfit

**Key constraints:**
- Players cannot pick items they personally drew
- Each outfit must have exactly 1 hat, 1 shirt, 1 pants, 1 shoes (complete set)
- Time limit per outfit building phase (default: 3 minutes)
- All players must either lock in or be auto-submitted when time expires

**Outcome:** Each player has 1 complete outfit + all selected items are removed from the pool

---

### 4. Outfit Customization & Naming

After picking items, players enhance their outfits with sketches and names.

**What happens:**
- Player sees their selected outfit items
- **Optional:** Player can sketch on top of the outfit to add details (e.g., patterns, accessories, adjustments)
- Player writes a name for the outfit
- Sketch and name are stored alongside the outfit items

**Key constraints:**
- Sketching is optional (players waiting for others to pick can do this)
- Names should be brief (recommend: 1–5 words)
- If sketching is enabled as required (config), it has a time limit (e.g., 1 minute per outfit)

**Outcome:** Each outfit has items + optional sketch + name

---

### 5. Outfit Distinctness Check (Outfit 2 Only)

Before outfit 2 becomes voteable, it must be visually distinct from all Outfit 1s (including those created by other players).

**Rule:** Outfit 2 must differ from **every player's Outfit 1** in **at least 2 items**.

**What happens:**
- System compares Outfit 2 items to all Outfit 1 items (all players)
- If any other player's Outfit 1 matches 3+ items with this Outfit 2 → outfit 2 is rejected
- Player is notified: "Your second outfit is too similar to another player's first outfit. Please swap at least 2 items."
- Player returns to picking phase to make changes
- Repeat until outfit 2 passes distinctness check against all Outfit 1s

**Outcome:** Outfit 2 is confirmed distinct from all prior outfits and locked in

---

### 6. Swiss-System Voting Tournament

Players vote on outfit matchups. Each vote cast for an outfit earns that outfit 1 point (multiplied by criterion weight). Points accumulate across all matchups and rounds to determine the winner.

**Tournament Structure:**

**Swiss System Basics:**
- Round 1: Outfits paired based on random/balanced matchmaking
- Subsequent rounds: Outfits paired based on current point totals (similar records play each other)
- Number of rounds = ceil(log2(number of outfits))
  - 6 players (12 outfits) = 4 rounds
  - 8 players (16 outfits) = 4 rounds
  - 10 players (20 outfits) = 5 rounds
  - 16 players (32 outfits) = 5 rounds

**What happens in each round:**
1. Matchups are generated (each outfit paired with another, no outfit votes on itself)
2. All voters (except the two outfit creators) vote on all voting criteria
3. Each vote cast for an outfit awards that outfit 1 point per criterion (multiplied by that criterion's weight)
4. Results are revealed (subject to visibility settings)
5. Next round begins

**Voting per Matchup:**
- Voters see both outfits side-by-side (with items, sketches, names)
- Voters rate each outfit on configured criteria (default: Theme Adherence + Personal Preference)
- Each vote cast for an outfit = 1 point for that outfit on that criterion (multiplied by criterion weight)
- Both outfits earn points proportionally to the votes they receive
- Ties in a criterion (equal votes) are broken by a coin flip — the coin flip winner earns 1 bonus point

**Outcome:** Final standings determined by cumulative points across all rounds

---

### 7. Leaderboard & Winner Declaration

Final scores are tallied and displayed.

**What happens:**
- All votes are finalized
- Player standings are ranked by total points (descending)
- If two or more players tie on total points, the tiebreaker is the player who won the most individual matchups
- If the matchup tiebreaker is also tied, a coin flip determines the winner
- Winner is announced with a celebratory message
- Full leaderboard shown to all players
- Option to play again or return to menu

**Outcome:** Game session ends

---

## Game Flow

### Session Overview

```
1. Host creates game session
2. Host configures settings (or uses defaults)
3. Players join lobby (wait for 6+ players)
4. Host starts game → PHASE 1: DRAWING
5. PHASE 2: POOL REVEAL
6. PHASE 3: OUTFIT 1 BUILDING
7. PHASE 4: OUTFIT 1 CUSTOMIZATION & SUBMISSION
8. PHASE 5: OUTFIT 2 BUILDING (fresh pool)
9. PHASE 6: OUTFIT 2 CUSTOMIZATION & SUBMISSION
10. PHASE 7: VOTING TOURNAMENT
11. PHASE 8: RESULTS & LEADERBOARD
12. End game or play again
```

---

### Phase 1: Drawing Phase (Detailed)

**Pre-phase:**
- Host can optionally announce themes now (if "theme before drawing" is enabled)
- Host presses "Start Drawing Phase"

**During phase:**
- Game displays: "Draw HATS — 60 seconds remaining" (or other clothing type)
- Canvas/drawing area is provided for each player
- Timer counts down audibly and visually
- Players draw freehand
- When timer expires, automatically move to next clothing type

**Sequence:**
1. Draw hats (60s, default)
2. Draw shirts (60s, default)
3. Draw pants (60s, default)
4. Draw shoes (60s, default)
*(Custom clothing types follow same pattern)*

**Post-phase:**
- All drawings are aggregated into the shared clothing pool
- Themes are announced now (if "theme after drawing" is enabled)
- Game transitions to Pool Reveal phase

---

### Phase 2: Pool Reveal (Detailed)

**What players see:**
- Animated reveal of all drawings in the shared pool, displayed grouped by clothing type
- Full grid of every item that will be available to pick

**Player actions:**
- Browse the pool freely (view-only)
- Press **"Ready"** when they have seen enough and are ready to begin picking

**Advancement:**
- Once all players press Ready, outfit building begins immediately
- If not all players are ready when the countdown timer (default: 30s) expires, the phase auto-advances

---

### Phase 3: Outfit 1 Building (Detailed)

**Pre-phase:**
- Clothing pool is generated (all drawings, minus the player's own)
- Theme is displayed prominently at top

**During phase:**
- Timer: 3 minutes (default)
- Player sees:
  - Outfit building area (4 slots: hat, shirt, pants, shoes)
  - Clothing pool grid
  - Current selections in their outfit slots
- Player action: Click/drag items from pool into outfit slots
- **If item is claimed by another player during selection, it disappears from the pool**
- Player can swap items in/out freely until time expires
- Once player has all 4 slots filled, they can lock in (or wait for time to expire)
- Lock-in is optional (auto-submits on time expiration)

**Post-phase:**
- All players' outfits are locked
- Pool items used are removed from available pool
- Game transitions to customization phase

---

### Phase 4: Outfit 1 Customization & Submission (Detailed)

**What player sees:**
- Their outfit items displayed
- Optional sketch canvas overlaid on top
- Text field: "Outfit Name"
- Button: "Submit Outfit 1"

**Player actions:**
- **Optional:** Sketch details (30s–1min recommended)
- **Required:** Name the outfit
- Click "Submit" to confirm

**Post-phase:**
- All players' outfits are collected
- Game announces: "Time to create Outfit 2!"
- Pool is reset (exact copy of original pool, minus Outfit 1 picks)
- A new Pool Reveal phase is shown before Outfit 2 building begins (same reveal flow, shorter timer)
- New outfit building phase begins

---

### Phase 5: Outfit 2 Building (Detailed)

**Same as Phase 3, with differences:**
- Fresh pool (copy of original, minus Outfit 1 picks)
- Players cannot pick items they used in Outfit 1 (unless `canReuseOutfit1Items` is enabled)
- Same theme as Outfit 1
- Time limit may differ (default: 3 minutes, same as Outfit 1)

**Post-phase:**
- All players' Outfit 2 selections are locked
- System checks: Does Outfit 2 differ from ALL Outfit 1s (all players) by 2+ items?
  - If yes (distinct from all Outfit 1s) → proceed to customization
  - If no (matches any player's Outfit 1 in 3+ items) → reject; player must rebuild Outfit 2 (return to picking phase)

---

### Phase 6: Outfit 2 Customization & Submission (Detailed)

**Same as Phase 4:**
- Optional sketch
- Required name
- Submit

---

### Phase 7: Voting Tournament (Detailed)

**Pre-voting:**
- All outfits (Outfit 1 + Outfit 2 per player) are prepared
- Total outfits = 2 × number of players
- Swiss-system tournament bracket is generated
- Matchups are displayed

**Round structure (repeat for each round):**

1. **Matchup Display:**
   - Show: Outfit A vs. Outfit B side-by-side
   - Display: Items, sketches, names
   - Display: Theme
   - Display: Voting criteria (Theme Adherence, Personal Preference, etc.)

2. **Voting:**
   - All players vote except the two outfit creators
   - For each criterion, voters choose: Outfit A or Outfit B
   - Voters can see all options before submitting their final vote

3. **Vote Tallying:**
   - Count votes per criterion per outfit
   - Each vote cast for an outfit = 1 point for that outfit on that criterion (multiplied by criterion weight)
   - Both outfits earn points proportional to the votes they receive
   - If votes are exactly tied on a criterion, a coin flip awards 1 bonus point to the winner

4. **Results Display (subject to visibility config):**
   - Show matchup result: "Outfit A: 12 pts — Outfit B: 8 pts"
   - Or show: "Outfit A wins! (Theme: 9–3, Preference: 3–9)"
   - Or show percentages: "Outfit A: 60% vs. Outfit B: 40%"
   - Hiding votes entirely: "Outfit A leads!" (no breakdown)

5. **Next Round:**
   - Swiss-system automatically pairs outfits for next round based on cumulative points
   - Repeat voting

**Continuation:**
- Continue until all rounds complete (default: 4 rounds for 6-player games)

**Post-tournament:**
- All votes finalized
- Points tallied across all outfits per player
- Leaderboard generated

---

### Phase 8: Results & Leaderboard

**Display:**
- Player standings ranked by total points
- Winner highlighted (e.g., crown emoji, color highlight)
- Tiebreaker applied if needed (matchups won → coin flip)
- Full point breakdown per player (optional detailed view)

**Post-game:**
- Option: "Play Again" (return to settings → new game)
- Option: "Return to Menu"

---

## Rules & Constraints

### Player Behavior Rules

**During Drawing Phase:**
- Players draw only the assigned clothing type (enforced by UI limiting shape)
- Maximum items per type per player: 5 (default, configurable)
- Players cannot see others' drawings live

**During Pool Reveal:**
- Players may not claim items during the reveal phase — view only
- All players must confirm ready (or the timer elapses) before picking begins

**During Outfit Building:**
- Player cannot pick items they personally drew
  - *Exception: None. This rule is absolute.*
- Player must select exactly 1 of each clothing type
- Simultaneous picks mean fastest players get first choice
- If two players click the same item simultaneously, the one whose input registered first gets it

**During Outfit Customization:**
- Player must provide a name for each outfit (sketching optional)
- Outfit 2 must differ from Outfit 1 by at least 2 items (enforced by system)
- Both Outfit 1 and Outfit 2 use the same theme

**During Voting:**
- Player cannot vote on their own outfits
  - *Exception: Host can configure to allow this (at risk of collusion).*
- Voter must vote on all criteria presented
- Ties are broken by coin flip (see Coin Flip Procedure below)

### Coin Flip Procedure

When a coin flip is required (tied criterion, tied final standings):
1. The system randomly selects one of the two affected players to call
2. That player is prompted: "Call heads or tails!" with a **15-second timer**
3. If the player does not respond before the timer expires, the system randomly selects heads or tails on their behalf
4. The system then randomly generates a result (heads or tails)
5. If the call matches the result, the calling player wins the flip; otherwise the other player wins
6. The winner receives the coin flip bonus point (for tied criteria) or is declared the tiebreaker winner (for tied final standings)

### System Constraints

**Pool Management:**
- Outfit 1 pool: All drawings except player's own
- Outfit 2 pool: Exact copy of Outfit 1 pool, minus Outfit 1 picks
- Items used in Outfit 1 are unavailable for Outfit 2 (unless `canReuseOutfit1Items` is enabled)
- Items can be used by different players across outfits

**Outfit Validity:**
- Outfit 1: Must have 1 hat + 1 shirt + 1 pants + 1 shoes
- Outfit 2: Same, plus must differ by 2+ items from **every player's Outfit 1** (cannot match any other player's Outfit 1 in 3+ items)
- Sketches do not count toward distinctness (only items matter)
- Both outfits use the same theme

**Voting Validity:**
- Only non-creators can vote on a matchup
- Each voter votes on all criteria
- Tied criteria trigger coin flip (not re-voting)

---

## Scoring System

### Points Per Matchup

**Scoring model:** Each vote cast for an outfit earns that outfit **1 point** per criterion. If criterion weights are configured, each vote is worth `weight` points instead of 1.

Both outfits in a matchup earn points — scoring is proportional, not winner-takes-all.

| Criterion | Points Awarded | Tie Resolution |
|-----------|----------------|----------------|
| Theme Adherence | 1 pt per vote received (× criterion weight) | Coin flip: winner receives 1 bonus point |
| Personal Preference | 1 pt per vote received (× criterion weight) | Coin flip: winner receives 1 bonus point |

*Example with default weight of 1 and 4 eligible voters:*
- Theme vote split 3–1 → Outfit A: 3 pts, Outfit B: 1 pt
- Preference vote split 2–2 → Tie → coin flip → winner gets 2 + 1 bonus = 3 pts, loser gets 2 pts
- Matchup total: Outfit A: 6 pts, Outfit B: 3 pts

*Criterion weights act as multipliers. A weight of 2 means each vote is worth 2 points for that criterion.*

---

### Bonus Points

**Round leader bonus:** +3 points
- Awarded to the outfit that accumulated the most points in a given voting round
- In case of a tie for round leader, all tied outfits receive the bonus
- *Optional: Can be disabled in config*

**Tournament winner bonus:** +10 points
- Awarded to the player with the highest cumulative points across all rounds
- *Optional: Can be disabled in config*

---

### Point Calculation Example

**Scenario:** 6 players → 12 outfits, 4 Swiss rounds. Each matchup has 4 eligible voters (6 players minus 2 creators). Below are two representative matchups from Round 1.

**Round 1, Matchup 1 — Outfit A vs. Outfit B:**

| Criterion | Votes for A | Votes for B | A Points | B Points |
|-----------|-------------|-------------|----------|----------|
| Theme Adherence | 3 | 1 | 3 | 1 |
| Personal Preference | 2 | 2 (tie → coin flip → A wins) | 2 + 1 bonus = 3 | 2 |
| **Matchup Total** | | | **6** | **3** |

**Round 1, Matchup 2 — Outfit C vs. Outfit D:**

| Criterion | Votes for C | Votes for D | C Points | D Points |
|-----------|-------------|-------------|----------|----------|
| Theme Adherence | 4 | 0 | 4 | 0 |
| Personal Preference | 3 | 1 | 3 | 1 |
| **Matchup Total** | | | **7** | **1** |

**After Round 1 (showing these 4 outfits only):**
- Outfit C: 7 pts + 3 (round leader bonus) = **10 pts**
- Outfit A: 6 pts
- Outfit B: 3 pts
- Outfit D: 1 pt

**Round 2 Swiss pairing** (similar records face each other):
- Outfit C (10 pts) vs. Outfit A (6 pts)
- Outfit B (3 pts) vs. Outfit D (1 pt)

*This pattern continues for Rounds 3 and 4. Final player standings sum both of that player's outfits' points. The player with the highest total wins. Ties broken by most matchups won, then coin flip.*

---

## Configurable Settings

### Default Configuration

All settings default to recommended values. Host can customize if desired.

---

### DRAWING PHASE Settings

#### `drawingTimePerRound`
- **Default:** 60 (seconds)
- **Options:** 45, 60, 90, 120
- **Impact:** Faster = fewer items per type; Slower = larger pool
- **Recommendation:** 60s is balanced for most groups

#### `maxItemsPerType`
- **Default:** 5
- **Options:** 3, 5, 8, 10, unlimited
- **Impact:** Limits items per player per clothing type
- **Recommendation:** 5 ensures variety without overwhelming the pool

#### `clothingTypes`
- **Default:** ["hat", "shirt", "pants", "shoes"]
- **Options:** Custom list (host can redefine types)
- **Impact:** More types = longer drawing phase; Custom types enable themed games
- **Recommendation:** Stick with defaults for standard play

---

### THEME SYSTEM Settings

#### `themeSource`
- **Default:** "builtInPool"
- **Options:**
  - `"builtInPool"` — Game provides curated themes
  - `"playerWritten"` — Each player submits one theme at game start; all submitted themes are shown to all players before outfit building begins
  - `"hostPicked"` — Host selects from list before game starts
  - `"randomVoting"` — Game shows 3–5 random themes; players vote on 2 to use
- **Impact:** Affects player buy-in and game tone
- **Recommendation:** "builtInPool" for first-time players; "playerWritten" for repeat groups

*Note on `"playerWritten"`: Since this game is designed for family and friends without matchmaking, theme moderation is not required. All submitted themes are displayed to all players as-is before outfit building begins.*

#### `themeAnnouncement`
- **Default:** "beforeDrawing"
- **Options:**
  - `"beforeDrawing"` — Theme announced before drawing phase starts
  - `"afterDrawing"` — Theme announced after drawings collected, before outfit building
- **Impact:** Before = more intentional art; After = more challenging/reactive
- **Recommendation:** "beforeDrawing" for casual groups; "afterDrawing" for experienced players

*Note: Both Outfit 1 and Outfit 2 always use the same theme. There is no per-outfit theme variation.*

---

### OUTFIT BUILDING Settings

#### `outfitBuildingTimeLimit`
- **Default:** 180 (seconds, 3 minutes)
- **Options:** 120, 180, 240, 300, 0 (unlimited)
- **Impact:** Faster = higher speed pressure; Slower = more thoughtful picks
- **Recommendation:** 180s balances strategy and tension

#### `allowSketching`
- **Default:** true
- **Options:** true, false
- **Impact:** Enables optional embellishment; off speeds up game
- **Recommendation:** true (adds expression with no downside)

#### `sketchingRequired`
- **Default:** false
- **Options:** true, false
- **Impact:** If true, sketching becomes timed component (1 min per outfit)
- **Recommendation:** false (optional is friendlier)

#### `canReuseOutfit1Items`
- **Default:** false
- **Options:** true, false
- **Impact:** If false, Outfit 2 cannot reuse any items from Outfit 1 (this setting takes precedence over `outfitDistinctnessRule`)
- **Recommendation:** false (forces creativity)

#### `outfitDistinctnessRule`
- **Default:** 2
- **Options:** 1, 2, 3
- **Impact:** Only applies when `canReuseOutfit1Items` is true. Defines how many Outfit 1 items may appear in Outfit 2:
  - 1 = Outfit 2 can reuse up to 3 of 4 items (lenient)
  - 2 = Outfit 2 must differ in at least 2 items (moderate)
  - 3 = Outfit 2 must differ in at least 3 items (strict, more creative pressure)
- **Recommendation:** 2 (balanced creativity vs. flexibility)

#### `outfit2PoolType`
- **Default:** "exactCopy"
- **Options:**
  - `"exactCopy"` — Outfit 2 pool is identical to Outfit 1 pool (minus used items)
  - `"freshDrawingsOnly"` — Outfit 2 pool contains only new/unused drawings
  - `"hybrid"` — Some items from Outfit 1 pool + new drawings
- **Impact:** Affects second-chance fairness and variety
- **Recommendation:** "exactCopy" (fairest; guarantees same options for all)

---

### VOTING & SCORING Settings

#### `votingCriteria`
- **Default:** ["themeAdherence", "personalPreference"]
- **Options:** Any combination of:
  - `"themeAdherence"` — How well outfit fits the theme
  - `"personalPreference"` — Voter's subjective taste
  - `"creativity"` — How unique/innovative the outfit is
  - `"skillExecution"` — Quality of drawing/design
- **Impact:** More criteria = more nuanced voting; Fewer = faster, simpler
- **Recommendation:** ["themeAdherence", "personalPreference"] (balanced, quick)

#### `criterionWeights`
- **Default:** {"themeAdherence": 1, "personalPreference": 1}
- **Options:** Host-defined multiplier values per criterion (whole numbers)
- **Impact:** Each vote for a criterion is worth `weight` points. Higher weight = that criterion matters more to the final score
  - Example: {"themeAdherence": 2, "personalPreference": 1} = theme votes worth twice as much
  - Example: {"personalPreference": 3, "themeAdherence": 1} = taste-focused game
- **Recommendation:** Equal weights (1/1) for balance

#### `votingVisibility`
- **Default:** "percentagesShown"
- **Options:**
  - `"fullyHidden"` — No votes revealed until end (tension building)
  - `"percentagesShown"` — Only vote percentages shown (no individual votes)
  - `"individualVotesShown"` — See who voted for what (transparency)
  - `"liveVoting"` — See votes come in real-time (engagement)
- **Impact:** Affects tension, social dynamics, transparency
- **Recommendation:** "percentagesShown" (fair balance of drama + anonymity)

#### `votingScope`
- **Default:** "cannotVoteOnOwn"
- **Options:**
  - `"cannotVoteOnOwn"` — Players cannot vote on outfits they created (recommended)
  - `"canVoteOnOwn"` — Players can vote for themselves (risky, collusion-prone)
  - `"hostDecides"` — Host can toggle per-player
- **Impact:** Self-voting enables collusion but increases engagement
- **Recommendation:** "cannotVoteOnOwn" (fair, prevents collusion)

#### `tournamentFormat`
- **Default:** "swiss"
- **Options:**
  - `"swiss"` — Swiss system (fair competitive, 4 rounds typical for 6 players)
  - `"singleElimination"` — Bracket tournament (fast, unbalanced; losers drop out early)
  - `"custom"` — Host specifies number of voting rounds (Swiss pairing used)
- **Impact:** Swiss = balanced; Elimination = drama
- **Recommendation:** "swiss" (best balance for party games)

#### `tournamentRounds`
- **Default:** auto-calculated (ceil(log2(num_outfits)))
- **Options:** 1–10 or "auto"
- **Impact:** More rounds = longer voting phase; fewer = quicker conclusion
- **Recommendation:** "auto" (system calculates optimal)

#### `bonusPoints`
- **Default:** {"roundLeader": 3, "tournamentWin": 10}
- **Options:** Host-defined point values (or 0 to disable)
- **Impact:** Bonus points incentivize consistent performance
- **Recommendation:** Defaults (balanced incentive)

---

### GAME FLOW Settings

#### `numOutfitRounds`
- **Default:** 2
- **Options:** 1, 2, 3, 4
- **Impact:** More rounds = longer game (additive 3+ mins per outfit)
- **Recommendation:** 2 (standard; tests all mechanics)

#### `minPlayers`
- **Default:** 6
- **Options:** 6–20+
- **Impact:** Voting viability; Swiss pairing; game balance. Below 6 players, each matchup has fewer than 4 eligible voters, reducing voting meaningfulness
- **Recommendation:** 6 minimum

#### `hostRole`
- **Default:** "active"
- **Options:**
  - `"active"` — Host announces phases, picks themes, moderates (engaged)
  - `"passive"` — Host just tracks; players self-manage (autonomous)
- **Impact:** Active host = guided experience; Passive = self-directed
- **Recommendation:** "active" (easier for first games)

#### `hostDisconnectTimeout`
- **Default:** 60 (seconds)
- **Options:** 30, 60, 120, 300
- **Impact:** How long to wait for the host to reconnect before abandoning the session
- **Recommendation:** 60s (gives host time to reconnect without blocking players too long)

---

### Advanced Settings

#### `allowPlayersToChooseThemes`
- **Default:** false
- **Options:** true, false
- **Impact:** If true, players vote on which themes to use before drawing
- **Recommendation:** false (simpler; host controls pacing)

#### `allowVotingRollback`
- **Default:** false
- **Options:** true, false
- **Impact:** If true, players can change votes before round closes (reduces finality)
- **Recommendation:** false (votes are final)

#### `randomizePairings`
- **Default:** true (Swiss system uses smart pairing)
- **Options:** true, false
- **Impact:** If false, matchups are deterministic (replayable)
- **Recommendation:** true (organic pairings feel more dynamic)

---

## UI/UX Requirements

### Screen States & Transitions

#### Lobby Screen
- Player list (display all joined players)
- Start Game button (enabled when 6+ players)
- Settings button (host only; opens settings menu)
- Status: "Waiting for host to start..." / "Ready to play"

#### Settings Menu
- Organized by category (Drawing, Theme, Outfit Building, Voting, Game Flow)
- Default values highlighted / recommended label
- Each setting has tooltip explaining impact
- Presets available (quick-select)
- Save/Load custom preset option
- Back button to lobby

#### Drawing Phase Screen (Different Host and Player Views)
##### Host View
- Clothing type displayed prominently ("DRAW HATS")
- Timer (visual + audio countdown)
- Player submission view (shows which players are finished)
- Progress indicator (Round 1/4)

##### Player View
- Clothing type displayed prominently ("DRAW HATS")
- Timer (visual + audio countdown)
- Canvas for drawing (full screen or primary area)
- Clear canvas button
- Visual feedback: "Drawing saved automatically"
- Submit button

#### Pool Reveal Screen
- Animated reveal of all drawings, grouped by clothing type
- Large, clear display of each item in the pool
- "Ready" button for each player (greyed out until reveal animation completes)
- Counter showing how many players are ready: "4 / 6 Ready"
- Countdown timer showing time until auto-advance
- Players who have pressed Ready are shown a "Waiting for others..." state

#### Outfit Building Screen (Different Host and Player Views)
##### Host View
- Current round displayed prominently ("BUILD OUTFIT #1")
- Player submission view (shows which players are finished)

##### Player View
- Left side: Clothing pool (grid of all available items, scrollable)
- Center: Outfit builder (4 slots: hat, shirt, pants, shoes)
- Right side: Timer + status (3/4 items selected)
- Drag-and-drop from pool to slots
- Lock-in button (when all 4 slots filled)
- Visual feedback: Red outline when item claimed by another player
- Sound effect: Subtle "woosh" when item is claimed

#### Customization Screen (After Picking - For Players Only)
- Large display of selected outfit items
- Optional sketch canvas (overlay/transparency mode)
- Text input: Outfit name
- Submit button
- Progress: "Outfit 1 of 2" (if applicable)

#### Voting Screen (Different Host and Player Views)
##### Host View
- Outfit A displayed (left)
- Outfit B displayed (right)
- Both outfits show: Items, sketch (if present), name, theme
- Progress: "Round 1 of 4 • Matchup 1 of 6"
- Vote count display (subject to visibility config)

##### Player View
- Progress: "Round 1 of 4 • Matchup 1 of 6"
- Voting criteria displayed clearly
- Vote buttons: "Choose A" or "Choose B" (per criterion)
- Submit vote button

#### Coin Flip Screen
- Displayed when a tie must be broken
- Shows which player has been selected to call
- Large "HEADS" and "TAILS" buttons
- 15-second countdown timer (clearly visible)
- If timer expires: system auto-selects and displays "Time's up! [Heads/Tails] was chosen for you."
- System then announces the result and the winner

#### Results Screen
- Leaderboard ranked by total points
- Winner highlighted
- Tiebreaker result shown if applicable ("Tie broken by matchups won" or "Tie broken by coin flip")
- Detailed breakdown (optional expandable view)
- Play again / Return to menu buttons

---

### Interaction Patterns

#### Drawing Canvas
- Uses existing SvgDrawingCanvas component and logic
- Auto-save on timer expiration

#### Pool Reveal
- Smooth staggered animation revealing items one-by-one or group-by-group
- Subtle sound effect per item reveal or per group reveal
- Allows scrolling through pool after animation completes

#### Outfit Picking (Simultaneous Real-Time)
- Click/tap item from pool; it is placed in its corresponding slot, replacing an existing slot item if applicable
- Instant visual feedback
- Conflict handling: If item is claimed mid-selection, show error state

#### Voting Interaction
- Click/tap to select preference per criterion
- Visual indication of selected choice
- Cannot submit until all criteria are voted
- Confirm button prevents accidental submission

#### Coin Flip Interaction
- Selected player sees prominent "HEADS" and "TAILS" buttons
- Other players see a waiting screen: "[Player Name] is calling it..."
- On resolution, all players see the result and outcome simultaneously

---

### Accessibility Requirements

- **Touch-friendly:** Buttons sized for touch (min 44x44px)
- Responsiveness: Works on mobile (portrait + landscape), tablet, desktop

---

### Visual Design Considerations

- **Clothing display:** Show drawn items as actual sketches (not generic icons)
- **Pool organization:** Grid layout, scrollable, with clear item boundaries
- **Pool reveal:** Animated, engaging reveal before picking — treat it as a moment
- **Outfit builder:** Large slots, clear visual hierarchy
- **Timer:** Large, visible countdown (color changes as time runs low)
- **Voting:** Side-by-side comparison (left vs. right)
- **Leaderboard:** Ranked list with scores, winner indicator

---

### Performance Requirements

- Drawing phase: Smooth 60fps canvas rendering
- Outfit picking: Instant visual feedback (<100ms latency)
- Voting: No lag when clicking vote options
- Network: Simultaneous picking requires real-time sync (WebSocket or similar)
- Responsiveness: Works on mobile (portrait + landscape), tablet, desktop

---

## Terminology

| Term | Definition |
|------|-----------|
| **Clothing Pool** | Shared collection of all drawn items available for outfit picking. |
| **Outfit** | A complete set of 1 hat + 1 shirt + 1 pants + 1 shoes. |
| **Pool Reveal** | Animated phase after drawing where all items are displayed to all players before picking begins. |
| **Sketch** | Optional embellishment drawn on top of an outfit after picking. |
| **Criterion / Criteria** | A voting dimension (e.g., "Theme Adherence", "Personal Preference"). |
| **Criterion Weight** | A multiplier applied to votes for a criterion; each vote is worth `weight` points. |
| **Matchup** | Head-to-head vote between two outfits. |
| **Round (Voting)** | A set of simultaneous matchups in the tournament. |
| **Swiss System** | Tournament format where outfits are paired based on cumulative points. |
| **Coin Flip** | Tie-breaking method: a randomly selected player calls heads or tails (15s timer); system resolves randomly. |
| **Distinctness** | Outfit 2 must differ from every player's Outfit 1 by at least 2 items. |
| **Self-Vote** | Voting on an outfit you created (typically disallowed). |
| **Pool Reset** | Creating a fresh copy of the pool for Outfit 2 (minus Outfit 1 picks). |
| **Auto-Submit** | Automatic submission when time limit expires. |
| **Reuse Limit** | Governed by `outfitDistinctnessRule`; only applies when `canReuseOutfit1Items` is true. |

---

## Edge Cases & Resolution

### Drawing Phase

**Case 1: Player draws nothing in a round**
- Resolution: Item count for that type = 0; they proceed to next round normally. Pool may be smaller for that clothing type.

**Case 2: Drawing canvas crashes mid-round**
- Resolution: Previous saved strokes are preserved. Player reconnects and continues drawing.

**Case 3: Multiple players draw identical items**
- Resolution: Allowed. Duplicates are stored separately (same item appearance, different creators).

---

### Pool Reveal

**Case 1: A player never presses Ready**
- Resolution: The countdown timer (default: 30s) elapses and the phase auto-advances for all players.

**Case 2: Player disconnects during reveal**
- Resolution: Their Ready state is ignored. If they reconnect before the timer elapses, they see the pool and can press Ready. Phase advances normally when timer ends.

---

### Outfit Building

**Case 1: Two players click the same item simultaneously**
- Resolution: The input that registered first (server-side timestamp) wins. Loser sees item disappear; they must select another.

**Case 2: Player doesn't lock in by time expiration**
- Resolution: If all 4 slots are filled, current outfit is auto-submitted. If slots are incomplete, random available items in the pool populate the empty slots — the auto-fill logic will never assign an item the player drew themselves. Player is notified of what was auto-filled.

**Case 3: Player tries to pick an item they drew**
- Resolution: Item is visually disabled/grayed out. Click has no effect. Tooltip explains: "You drew this item and cannot use it."

**Case 4: Pool becomes empty (all items used)**
- Resolution: Unlikely with typical player counts, but if it happens: remaining players cannot complete outfits. Game should prevent this via item count validation.

**Case 5: Auto-fill cannot avoid self-drawn items (all remaining items were drawn by this player)**
- Resolution: Extremely unlikely with 6+ players. If it occurs, the system notifies the player and host: "Not enough items remain for [Player] to complete their outfit without using their own drawings." Host must intervene or the player's outfit is submitted incomplete and marked invalid for voting.

---

### Outfit Distinctness Check

**Case 1: Player's Outfit 2 uses 3+ items matching another player's Outfit 1**
- Resolution: System rejects outfit. Player is notified: "Too similar to [Player Name]'s first outfit (3 matching items). Swap 2+ items." Player returns to outfit building.

**Case 2: Player's Outfit 2 uses 3+ items matching their own Outfit 1**
- Resolution: System rejects outfit. Player is notified: "Too similar to your first outfit (3 matching items). Swap 2+ items." Player returns to outfit building.

**Case 3: Player rebuilds Outfit 2 and chooses items that match multiple other players' Outfit 1s**
- Resolution: Check against all players. If any comparison shows 3+ matching items, outfit is rejected.

**Case 4: Multiple players have identical Outfit 1s (by chance or design)**
- Resolution: A new Outfit 2 must be distinct from all of them. Must differ by 2+ items from each one.

**Case 5: Player has outfit that fails check but timer expires**
- Resolution: Auto-fill swaps conflicting items with random available items from the pool. The self-pick rule is respected during auto-fill (system will not assign items the player drew). Player is notified of the swaps made.

---

### Voting

**Case 1: Voter is one of the two outfit creators**
- Resolution: Voter is disabled for that matchup. "You created one of these outfits and cannot vote."

**Case 2: Two outfits tie on a voting criterion**
- Resolution: Coin flip procedure is triggered (see Coin Flip Procedure in Rules & Constraints). The winner of the flip receives 1 bonus point.

**Case 3: Voter doesn't submit vote before round closes**
- Resolution: Vote is not counted. Missing votes reduce the vote pool but don't invalidate the matchup. (Example: 4 eligible voters, 3 submit votes on Theme; the 1 missing vote doesn't count — Outfit A may receive 2 pts, Outfit B 1 pt from 3 votes cast.)

**Case 4: All voters abstain / no votes submitted for a matchup**
- Resolution: Rare edge case. Matchup results in a 0–0 tie; coin flip determines the winner, who receives 1 bonus point.

---

### Coin Flip

**Case 1: Selected player disconnects before calling**
- Resolution: If the player reconnects before the 15-second timer expires, they can still call. If the timer expires (due to disconnect or inaction), the system auto-selects a call on their behalf and proceeds.

**Case 2: Coin flip needed for final standings tiebreaker**
- Resolution: Same procedure as matchup coin flips. A random player from the tied group is selected to call. The system resolves the flip and declares the winner.

---

### Network & Connectivity

**Case 1: Player disconnects during drawing phase**
- Resolution: Their drawings up to disconnect are saved. If they reconnect before phase ends, they resume drawing. If they reconnect after phase, their saved drawings are included in pool.

**Case 2: Player disconnects during outfit picking**
- Resolution: Their current outfit (if any) is frozen. If they reconnect before phase ends, they can resume picking. If they reconnect after phase, their last outfit is submitted (if complete) or outfit is rejected.

**Case 3: Player disconnects during voting**
- Resolution: Their votes cast are saved. If they reconnect, they can continue voting on remaining matchups.

**Case 4: Host disconnects**
- Resolution: Game is paused. Players wait. Host can reconnect and resume. If host doesn't reconnect within the configured timeout (default: 60 seconds), the game is abandoned.

---

### Player Count Edge Cases

**Case 1: Fewer than 6 players join**
- Resolution: Game can start, but with a warning. Host is notified: "At least 6 players is recommended. Currently [N]. Voting may be less meaningful with fewer players."

**Case 2: Mid-game player leaves/quits**
- Resolution: Player is marked inactive. Their outfits remain in the pool for voting. They cannot vote but see results. Scoring continues (their outfits can still earn votes).

**Case 3: Very large player count (20+)**
- Resolution: Swiss system scales well. Voting rounds increase slightly but remain manageable. No functional issues.

---

### Scoring Edge Cases

**Case 1: Outfit receives all votes in a matchup**
- Resolution: Outfit gets all available points for that criterion (e.g., 4 pts from 4 voters). Other outfit gets 0 pts.

**Case 2: Both outfits tie on all criteria**
- Resolution: One coin flip per tied criterion. Each coin flip awards 1 bonus point to its winner. Outfits may split tied criteria.

**Case 3: Player's two outfits face each other in voting**
- Resolution: Both outfits are valid, though this should be avoided by pairing logic. Player cannot vote on either matchup. Voting continues with all other eligible players.

**Case 4: Final standings result in an exact tie between two players**
- Resolution: Tiebreaker 1 — the player who won more individual matchups wins. Tiebreaker 2 (if matchups are also tied) — coin flip determines the winner.

---

### Configuration Validation

**Case 1: Host sets conflicting settings**
- Resolution: System prevents invalid combinations. Example: `"playerWritten"` themes + `"beforeDrawing"` → Game collects one theme per player at start, reveals all themes to all players, then announces before drawing. No conflict.

**Case 2: Host sets very small time limits (e.g., 15s drawing)**
- Resolution: Allowed. Game may be unplayable, but host can configure as they wish.

**Case 3: Host sets maxItemsPerType to 0**
- Resolution: No items drawn; pool is empty; game proceeds with empty outfits. Unlikely to be fun, but allowed.

---

## Implementation Notes

### Technology Considerations

- **Real-time sync required:** Outfit picking and pool reveal are simultaneous
- **Drawing canvas:** Use existing SvgCanvas component for smooth, responsive sketching
- **State management:** Central game state (phase, players, outfits, votes) on server
- **Scalability:** Designed for 6–20 players
- **Coin flip timer:** Server-side 15-second timer to prevent client-side manipulation

### Testing Checklist

- [ ] Drawing phase time tracking (ensure rounds end on time)
- [ ] Pool reveal animation (verify all items displayed, ready-gate works, auto-advance fires)
- [ ] Simultaneous picking conflict resolution (verify server timestamps are accurate)
- [ ] Auto-fill self-pick rule compliance (verify auto-fill never assigns player's own drawings)
- [ ] Outfit distinctness validation (ensure 2+ item difference is checked correctly)
- [ ] `canReuseOutfit1Items` taking precedence over `outfitDistinctnessRule`
- [ ] Swiss system pairing (verify correct matchups based on cumulative points)
- [ ] Vote tallying (ensure vote counts are accurate; 1 pt per vote × criterion weight)
- [ ] Coin flip procedure (random player selection, 15s timer, auto-select on timeout)
- [ ] Tiebreaker logic (points → matchups won → coin flip)
- [ ] Bonus point calculation (round leader and tournament winner awarded correctly)
- [ ] Edge case handling (disconnects, timeouts, empty pools, etc.)
- [ ] Accessibility (keyboard nav, screen reader, high contrast)
- [ ] Mobile responsiveness (drawing on mobile, touch interactions)

---

## Future Expansion Ideas (Post-Launch)

- **Stat tracking:** Player histories, win rates, favorite themes
- **Custom avatars/profiles:** Personalization
- **Seasonal themes:** Holiday-themed clothing types
- **Multiplayer features:** Persistent profiles, friend lists
- **AI voting:** Bot players for testing/practice
- **Spectator mode:** Non-players watch and react during reveal and voting
- **Accessibility improvements:** Voice input for drawing, haptic feedback

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | March 2026 | Initial design complete |
| 1.1 | March 2026 | Clarified vote-based scoring (1 pt per vote × weight); added Pool Reveal phase; raised minimum players to 6; defined tiebreaker rules; specified coin flip UX with 15s timer; clarified `canReuseOutfit1Items` precedence over `outfitDistinctnessRule`; consolidated theme settings (same theme for both outfits); specified `playerWritten` theme flow; updated Swiss rounds table for 2-outfits-per-player; added host disconnect default timeout (60s); fixed auto-fill self-pick compliance; updated scoring example; fixed typo |

---

**End of Document**
