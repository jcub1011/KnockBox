# Hidden Agenda — Game Design Document

## Overview

Hidden Agenda is a digital board game for 3–6 players built on observation, misdirection, and deductive reasoning. Players are rival art collectors navigating a prestigious gallery, each secretly pursuing their own agenda while trying to figure out what everyone else is up to.

Every player draws three secret tasks from a shared public pool, then moves through the gallery playing cards that curate, relocate, or remove pieces from shared art collections. All card plays are visible. The goal is twofold: complete your own tasks while correctly identifying what every other player is trying to accomplish.

There are no teams, no villains, no eliminations. Everyone is simultaneously hiding and hunting. Every action you take advances your goal but also feeds information to your opponents. The core tension is the balance between efficiency and stealth — rush your task and you'll finish it, but you'll also broadcast your intentions to the table.

The game is designed for same-room play. Players sit together around a shared screen or on individual devices, talking, bluffing, and reading each other in person.

The game is played over multiple rounds, with points accumulating across rounds. The player with the highest score at the end of the final round wins.

---

## Theme and Setting

Players are eccentric art collectors attending a multi-day exhibition at a grand gallery. Each collector has a secret agenda — a private obsession, a vendetta against a rival collection, a fixation on a particular wing — that drives their behavior. The gallery's collections are communal and in flux: pieces are being acquired, loaned out, damaged, and relocated constantly. The collectors move through the gallery's wings, playing Curation Cards that shape the collections, while watching each other for telltale patterns.

The gallery setting naturally supports the core mechanics:

- **Movement through wings** feels intuitive — you're wandering an art gallery, choosing which rooms to visit.
- **Building and removing collection progress** maps to acquiring pieces, loaning them out, or having them pulled for restoration.
- **Observation and deduction** fits the atmosphere — collectors sizing each other up, reading body language, noticing who keeps drifting toward the same exhibit.
- **The public task pool** is framed as a dossier of known collector obsessions — everyone knows what agendas *exist*, but not who holds which ones.

---

## Scoring

### Per-Round Scoring

Each player has 3 secret tasks. Task completion points are based on the task's difficulty category:

| Task Category | Difficulty | Points for Completion |
|---|---|---|
| Devotion | Easy | +1 |
| Style | Medium | +2 |
| Movement | Medium | +2 |
| Neglect | Hard | +3 |
| Rivalry | Hard | +3 |

| Action | Points |
|---|---|
| Completing a secret task | +1 to +3 (by difficulty) |
| Each correct guess of another player's task | +1 |
| Incorrect guess | 0 (no penalty, but no points) |

### Guess Submission

- Once per round, a player may submit their guesses for every other player's tasks. Submitting guesses does not cost a turn — a player may submit guesses and still take their normal turn actions.
- For each opponent, the player guesses all 3 of that opponent's tasks from the public dossier.
- This is a single, all-or-nothing submission — you guess everyone at once and cannot revise.
- You only get one guess submission per round. Once submitted, you cannot guess again that round.
- You do not have to guess. If you never submit guesses in a round, you earn 0 guess points for that round.
- Guesses are revealed at the end of the round, not immediately. This prevents early guessers from leaking confirmed information to the table.

### Scoring Dynamics

- In a 5-player game, a perfect read of all 4 opponents is worth 12 points (4 opponents × 3 tasks each). A player who completes all 3 of their own tasks earns 3–9 points depending on difficulty. This makes deduction the dominant skill while still rewarding task completion — especially for harder tasks.
- A player who completes all 3 tasks (e.g., one easy + two medium = 5 pts) and guesses 8 of 12 opponent tasks correctly (+8) scores 13. A player who fails all their tasks but reads the entire table scores 12. The difficulty-weighted task bonus keeps both skills relevant, particularly for players who draw hard tasks.
- Since wrong guesses carry no penalty, there's no reason to abstain from guessing if you have any information at all. The question is *when* to guess — early (before you've seen enough) or late (when you've seen more but patterns become clearer).
- Guessing is free (no turn cost), so the timing decision is purely informational: submit early to lock in reads and trigger the countdown, or wait for more data at the risk of someone else triggering it first.

### Multi-Round Scoring

- Scores accumulate across all rounds in a match.
- A match consists of a fixed number of rounds (recommended 3–5, configurable by the group).
- The player with the highest cumulative score at the end of the final round wins.
- Between rounds, all tasks are reshuffled and redrawn. The task pool may rotate (see Task Pool Rotation).

---

## The Gallery (Board)

The board represents a grand art gallery with interconnected wings. Players move through the gallery by spinning a spinner (range 3–12) and moving to any spot from 1 up to the number they rolled, choosing direction at forks. The layout encourages varied movement patterns while keeping players visible to each other.

### Wings (Zones)

| Wing | Character | Card Tendencies |
|---|---|---|
| The Grand Hall | Prestigious, high-profile collections | High-value single-collection Acquire cards. Big moves that draw attention. |
| The Modern Wing | Experimental, eclectic | Moderate-value multi-collection Acquire cards. Flexible but not flashy. |
| The Sculpture Garden | Outdoor, volatile | Mixed — wider spread of Acquire, Remove, and high-variance cards. Risky but rewarding. |
| The Restoration Room | Behind-the-scenes, transitional | Cards that apply to any collection but at lower values. Good for versatility and misdirection. |

### Spot Types

- **Curation Spots** — The player draws 3 Curation Cards from the spot's local pool, chooses one to play, and discards the other two face-down. The played card is revealed to all players. Curation Spots are the primary way collections gain or lose progress.
- **Event Spots** — The player receives one random Event Card. No Curation Cards are drawn. (See Event Cards section.)

---

## Collections and Progress

The gallery features a set of shared art collections with visible progress tracks. Collections are not owned by any player — they're communal, and all card plays affect them publicly. Progress represents the prestige, completeness, and renown of each collection.

### Collection Set (All Player Counts)

| Collection | Target Value | Wing |
|---|---|---|
| Renaissance Masters | 12 | The Grand Hall |
| Contemporary Showcase | 10 | The Grand Hall |
| Impressionist Gallery | 10 | The Modern Wing |
| Marble & Bronze | 8 | The Sculpture Garden |
| Emerging Artists | 8 | The Sculpture Garden |

### Progress Rules

- Collection progress is visible to all players at all times.
- Progress can never drop below 0.
- All progress changes are public — every card play shows who changed what.
- Collections visually reflect their state on the board (a thriving gallery filled with pieces, a sparse room with empty frames, etc.).

---

## Curation Cards

Drawn from Curation Spots. Each spot offers exactly 3 cards. The player chooses one to play; it is revealed to all players. The other two are discarded face-down.

### Card Types

**Acquire Cards** — Add progress to one or more collections. Represents purchasing pieces, accepting loans, or receiving donations.
- "+2 Renaissance Masters"
- "+1 Impressionist Gallery, +1 Marble & Bronze"
- "+3 to any collection in this wing"

**Remove Cards** — Remove progress from a collection. Represents pieces being loaned out, pulled for restoration, or lost to a rival gallery.
- "-1 Contemporary Showcase"
- "-2 Marble & Bronze"

**Trade Cards** — Present a trade-off between collections.
- "+2 Renaissance Masters OR +1 Impressionist Gallery and +1 Emerging Artists"
- "+3 Contemporary Showcase OR -1 Impressionist Gallery and +2 Marble & Bronze"

### Why Public Cards Drive Deduction

Every card play is a data point. When a player picks "+2 Renaissance Masters" over "+1 Impressionist Gallery and +1 Marble & Bronze," observers can ask: why did they value the Renaissance collection so highly? Cross-reference that choice against the public dossier and you start narrowing down their secret task. Over many turns, a pattern emerges — or a clever player deliberately breaks their pattern to throw you off, sacrificing efficiency for stealth.

### Draw Pool Composition

Each wing's Curation Spots have a local pool weighted toward that wing's associated collections. Under normal conditions, pools are weighted roughly:

- 50–60% Acquire Cards
- 15–20% Remove Cards
- 20–30% Trade Cards

The presence of Remove Cards in every pool is important — any player can draw a hand where Remove is the best option, creating natural cover for players whose tasks benefit from removal.

---

## The Dossier (Secret Task Pool)

At the start of each round, the full task pool is displayed to all players as a public reference — the Dossier of Known Collector Obsessions. Each player secretly draws three tasks from this pool. The pool is large enough that not all tasks are assigned each round — you know the *possible* agendas but not which subset is in play.

### Task Categories

Tasks are organized into five categories. Every task is designed around a visible behavioral pattern — something other players can spot by watching what you do turn after turn. The tell is always in the *choices you make*, not in the math behind them.

#### Category 1: Devotion Tasks

You must repeatedly favor a specific collection or wing. The pattern is straightforward and highly visible — the question for opponents is *which* devotion task you have, not whether you have one.

| ID | Task | Observable Pattern |
|---|---|---|
| D1 | Play a card that adds progress to Renaissance Masters on at least 4 separate turns. | Repeatedly choosing Renaissance Masters when other options are available. |
| D2 | Play a card that adds progress to Contemporary Showcase on at least 4 separate turns. | Consistently picking Contemporary builds across different spots. |
| D3 | Play a card that adds progress to Impressionist Gallery on at least 4 separate turns. | Gravitating toward Impressionist builds even from mixed hands. |
| D4 | Play a card that adds progress to Marble & Bronze on at least 4 separate turns. | Choosing Marble & Bronze cards when better-value options exist. |
| D5 | Play a card that adds progress to Emerging Artists on at least 4 separate turns. | Favoring Emerging Artists over more efficient plays. |
| D6 | Play cards affecting Grand Hall collections (Renaissance Masters or Contemporary Showcase) on at least 5 separate turns. | Heavy wing loyalty — spending most effort in one area. |
| D7 | Play cards affecting Sculpture Garden collections (Marble & Bronze or Emerging Artists) on at least 5 separate turns. | Persistent focus on the Sculpture Garden despite other options. |

#### Category 2: Neglect Tasks

You must avoid interacting with something — a collection, a wing, or a card type. The tell is the *absence* of an expected action. These are harder to detect because players must notice what you're *not* doing.

| ID | Task | Observable Pattern |
|---|---|---|
| N1 | Never play an Acquire card on Renaissance Masters for the entire round. | Skipping Renaissance builds even when they're the obvious best play. |
| N2 | Never play an Acquire card on Contemporary Showcase for the entire round. | Avoiding Contemporary entirely when other players are building it. |
| N3 | Never play an Acquire card on Impressionist Gallery for the entire round. | Ignoring Impressionist progress even when it's lagging. |
| N4 | Never enter the Grand Hall for the entire round. | Taking inefficient routes to avoid a whole section of the gallery. |
| N5 | Never enter the Modern Wing for the entire round. | Routing around the Modern Wing every turn, even when it's the shortest path. |
| N6 | Never play a Remove card for the entire round. | Always choosing an Acquire or Trade option even when Remove is the only way to advance positioning. |

#### Category 3: Style Tasks

You must establish a visible rhythm or habit in your play. The tell is a *repeated pattern* across turns that stands out against normal play.

| ID | Task | Observable Pattern |
|---|---|---|
| Y1 | Play a Remove card on at least 3 separate turns. | Choosing removal more often than a typical collector would. |
| Y2 | Play cards affecting at least 4 different collections across the round. | Spreading effort unusually wide instead of focusing. |
| Y3 | Play a card affecting the same collection at least 3 turns in a row. | Conspicuous streak of targeting one collection back-to-back. |
| Y4 | Alternate between Acquire and Remove cards for at least 4 consecutive turns. | Visible acquire-remove-acquire-remove rhythm. |
| Y5 | Play the highest-value card in your hand on at least 4 turns (other players can infer this when they see your options via Catalog). | Consistently picking the biggest number even when spreading would be smarter. |
| Y6 | Visit an Event Spot at least 3 times during the round. | Repeatedly routing toward Event Spots instead of productive Curation Spots. |

#### Category 4: Movement Tasks

You must go to specific places or move in recognizable ways. The tell is your pathing — where you choose to go when you have options.

| ID | Task | Observable Pattern |
|---|---|---|
| M1 | Visit all four wings at least once during the round. | Taking detours to reach wings that aren't on your natural path. |
| M2 | Spend at least 4 turns in the same wing. | Camping one wing conspicuously while others move around. |
| M3 | End your turn on the same spot as another player at least 3 times. | Following other players or lingering to land together. |
| M4 | Take the longest available path at every fork for at least 4 consecutive turns. | Always choosing the scenic route when shortcuts exist. |
| M5 | Change wings every turn for at least 4 consecutive turns. | Bouncing rapidly between areas without settling. |
| M6 | Return to the same spot at least 3 times during the round. | Looping back to one specific location repeatedly. |

#### Category 5: Rivalry Tasks

Your task involves how your actions relate to other players' actions. The tell is *reactive* — you're doing things in response to what others do, creating a visible cause-and-effect pattern.

| ID | Task | Observable Pattern |
|---|---|---|
| R1 | Play an Acquire card on a collection immediately after another player plays a Remove card on that same collection, at least 3 times. | Swooping in to "rescue" damaged collections suspiciously often. |
| R2 | Play a card affecting the same collection that the player immediately before you affected, on at least 4 turns. | Copying or echoing the previous player's collection choice. |
| R3 | Never play a card affecting the same collection as the player immediately before you, for at least 5 consecutive turns. | Conspicuously dodging whatever the last player touched. |
| R4 | Play a Remove card on a collection that is currently the highest-progress collection, at least 3 times. | Consistently knocking down the leader. |
| R5 | Play an Acquire card on a collection that is currently the lowest-progress collection, at least 3 times. | Always championing the underdog collection. |
| R6 | Be in the same wing as a specific other player (assigned randomly at task draw) on at least 4 turns. | Shadowing one person around the gallery. |

### Task Pool Design Principles

- **Behavioral, not numerical.** Every task creates a pattern in what you *do*, not in what number a collection reaches. Other players spot your task by watching your choices, not by calculating progress values.
- **Observable but overlapping.** Many tasks produce similar early-game signals. Someone repeatedly building Renaissance Masters could be D1 (Renaissance devotion) or Y3 (same-collection streak) or R2 (echoing the previous player) for the first few turns. The patterns diverge over time, rewarding patient observation.
- **Absences are harder to spot than presences.** Neglect tasks (Category 2) are stealthier because noticing that someone *never* acquires Contemporary Showcase requires tracking a negative. This creates a natural difficulty gradient: Devotion and Style tasks are easier to read, Neglect and Rivalry tasks are harder.
- **Readable within a few turns.** Most tasks require 3–5 matching actions. In a round that lasts 8–10 turns per player, a task requiring 4 matching actions means the player must commit to their pattern on most turns, giving opponents a realistic window to observe and guess.
- **Pool size matters.** In a 5-player game, 15 tasks are drawn (5 players × 3 tasks each) from a pool of roughly 30. The pool is public, so opponents can scan it and ask "which of these tasks explains what I'm seeing?" With 3 tasks per player, the behavioral signal is richer — players juggle multiple overlapping patterns, creating more complex deduction puzzles.

### Task Pool Rotation (Multi-Round)

Between rounds, the pool can be rotated to keep the game fresh:

- **Full rotation:** The entire pool is reshuffled and a new subset could emerge. Maximum variety.
- **Partial rotation:** Remove 5–8 tasks from the pool and add 5–8 new ones. Players retain some pattern recognition from prior rounds while adapting to new possibilities.
- **Fixed pool:** The pool stays the same across all rounds. Players get better at reading patterns but also better at disguising them. Rewards meta-learning across rounds.

The default recommendation is partial rotation — it balances freshness with developing expertise.

---

## Event Cards

Obtained by landing on Event Spots. A player may hold one Event Card at a time. Playing an Event Card consumes a turn.

### Event Card Types

**Catalog**

- Play on your turn instead of moving.
- Choose one other player. View the last 3 Curation Cards they drew (not just the one they played — the full draw, including what they discarded). This reveals their decision-making: what did they have, and what did they choose? The gap between options and selection is powerful deduction fuel.
- The target player knows they were Cataloged but not what you learned.

**Detour**

- Play immediately after spinning, before moving.
- Instead of moving your rolled number, swap your movement with another player's last movement (their previous turn's spinner result and destination). You go where they went; they don't move again — it only affects you.
- Useful for reaching a specific wing or spot that another player conveniently accessed.

### Event Card Strategy

Event Cards serve dual purposes: they help you complete your tasks (Detour) and they help you read others (Catalog). The one-card limit forces a choice between a positional tool and an investigative tool. Holding a Catalog signals that you're in deduction mode; using a Detour signals you need to reach a specific location. Other players can read your Event Card holdings as part of the meta-game.

---

## Turn Structure

### 1. Event Card Phase (Optional)

If holding an Event Card and wishing to play it:
- **Catalog** → Resolves now. Turn ends (skip Spin, Move, Draw).
- **Detour** → Held for use after spinning.

### 2. Spin Phase

Spin the spinner (range 3–12). Result visible to all. If holding Detour, may play it now.

### 3. Move Phase

Move to any spot from 1 up to the spinner result along the path, choosing direction at forks.

### 4. Draw Phase

Depends on spot type:
- **Curation Spot:** Draw 3 Curation Cards. Choose one to play (revealed publicly). Discard the other two face-down.
- **Event Spot:** Draw one Event Card. Swap or keep if already holding one.

### 5. Guess Submission (Free Action)

At any point during their turn, a player may submit guesses without spending their turn actions.

- The player privately assigns 3 tasks from the public dossier to each other player.
- Guesses are locked in and not revealed until the round ends.
- Once submitted, the player continues playing normally for the rest of the round but cannot guess again.
- **If this is the first guess submission of the round, the Guess Countdown is triggered.** All other players get exactly 2 more turns before the round ends.

### 6. Resolution

Collection progress is updated. Play history is logged. Next player's turn begins.

---

## Round Structure

### How a Round Ends

A round ends when either of two conditions is met — whichever comes first:

**Condition 1: Collection Trigger**

When a set number of collections reach their target values, the round ends immediately. All players who have not yet submitted guesses get one final opportunity before the reveal.

| Players | Collections to Trigger |
|---|---|
| 3 | 3 of 5 collections completed |
| 4 | 3 of 5 collections completed |
| 5 | 3 of 5 collections completed |
| 6 | 3 of 5 collections completed |

All player counts use the same 5 collections and the same trigger threshold. This ties the round clock to the board itself. Players collectively control the pace, which creates a key tension: building collections is the natural thing to do, but completing them too fast might end the round before you've finished your tasks or gathered enough information to guess. A player with a neglect task wants the round to go long, while a player whose devotion tasks are nearly complete might rush a collection to trigger the end.

**Condition 2: Guess Countdown**

When the first player submits their guesses, a countdown begins. Every other player gets exactly 2 more turns, then the round ends. Any player who hasn't submitted guesses by the deadline gets one final opportunity.

The first guesser is making a bold strategic move — they're confident enough to lock in their reads and they're forcing everyone else to act under pressure. Submitting early is a weapon: if you've read the table but others haven't, the countdown puts them at a disadvantage. But guessing too early with bad reads wastes your one submission on incomplete information.

**Combined Dynamics**

The two clocks interact. As collections approach completion, players feel the board clock ticking and may rush to submit guesses before they run out of turns. Meanwhile, a player who submits guesses early might cause opponents to panic-build collections to finish their tasks, which could accidentally trigger the collection clock too. Both clocks create urgency, but from different directions — one is driven by the board state, the other by social pressure.

### Maximum Round Length (Safety Valve)

To prevent rounds from stalling if neither trigger fires (e.g., heavy removal play keeps collections below completion and no one is confident enough to guess), each round has a maximum turn limit as a backstop:

| Players | Maximum Turns Per Player |
|---|---|
| 3 | 12 |
| 4 | 11 |
| 5 | 10 |
| 6 | 9 |

With 3 tasks per player (some requiring 4–5 matching actions), players need sufficient turns to complete tasks while still having room for misdirection. The turn limit scales down slightly at higher player counts to keep total round time manageable, but remains generous enough that all task types are achievable. If the maximum turn limit is reached without either trigger firing, the round ends and all remaining players get a final guess opportunity. In practice, one of the two triggers should fire well before the backstop in most games.

### Round Flow

1. **Dossier Display** — The full task pool (the Dossier of Known Collector Obsessions) is shown to all players.
2. **Secret Draw** — Each player draws three tasks secretly from the pool.
3. **Gameplay** — Players take turns according to the Turn Structure. The round continues until either the Collection Trigger or the Guess Countdown ends it, or the maximum turn limit is reached.
4. **Final Guesses** — Any player who has not yet submitted guesses gets one final opportunity.
5. **Reveal** — All secret tasks are revealed. Guess submissions are scored.
6. **Scoring** — Points are tallied and added to cumulative scores.
7. **Reset** — Collections reset to 0. Task pool rotates (if using rotation). New round begins.

### Match Length

A match consists of 3–5 rounds (configurable). The player with the highest cumulative score at the end of the final round wins.

Recommended defaults:
- **Quick match:** 3 rounds (25–40 minutes)
- **Standard match:** 4 rounds (40–55 minutes)
- **Extended match:** 5 rounds (55–70 minutes)

---

## Guess Timing Strategy

When to submit guesses is one of the game's deepest strategic decisions, amplified by the fact that your submission triggers the countdown for everyone else. Since guessing is a free action (it doesn't cost your turn), the decision is purely about information timing, not tempo.

- **Aggressive early guess** (turns 1–3): Almost no information about others, but you trigger the countdown immediately, forcing everyone else to guess within 2 turns on minimal data. This is a power play — you're betting that your reads are better than average even with limited info, and that the time pressure hurts your opponents more than your own accuracy suffers. With 3 tasks per opponent to guess, early reads are especially unreliable, making this a high-risk gambit.
- **Mid-round guess** (turns 4–6): The sweet spot for most games. You've seen enough to have strong reads on a couple of players and educated guesses on the rest. Submitting now triggers the countdown while opponents may still be gathering information, giving you an edge. With 3 tasks per player, the mid-round is where overlapping task patterns start to separate — one player's Devotion task becomes distinguishable from their Style task.
- **Late guess** (turns 7+): Maximum data but maximum risk. You've watched everyone for most of the round, but they've watched you too — and if someone else submits first, you may only have 2 turns to lock in your reads under pressure. The upside is accuracy across all 3 of each opponent's tasks; the downside is that you're racing both the guess countdown and the collection trigger.
- **Riding the collection clock**: If collections are close to completion, you know the round might end before anyone submits guesses. This creates a different kind of urgency — you might submit guesses not because you're confident but because you're worried the collection trigger will fire and force a final guess under even worse conditions.

---

## Multi-Round Meta-Game

Across rounds, players develop reads on each other's playstyles:

- **Habitual tells:** A player who always takes efficient routes might find it hard to mask a Movement task. Opponents who've played previous rounds with them know to watch for efficiency breaks.
- **Counter-adaptation:** If you were correctly read last round, you might overcompensate with decoy actions this round — but experienced opponents expect that.
- **Pool memory:** Under partial rotation, some tasks carry over between rounds. A player might draw one of the same tasks again, and opponents who recognize the pattern from a previous round have a head start on identifying that task — but with 3 tasks per player, the other two may be completely different.
- **Score awareness:** A trailing player might take bigger risks (early guesses, aggressive task completion) while a leading player might play conservatively and focus on steady point accumulation through accurate deduction.

---

## Board Layouts

The gallery board is a looping network of paths connecting the four wings, with forks that give players meaningful route choices every turn.

### The Grand Circuit

A central loop connects all four wings in a ring, with shortcut corridors cutting across the center. The layout resembles a classic gallery floorplan — a large rectangular path with rooms branching off it.

**Structure:**
- The main loop has roughly 24 spaces, passing through each wing in order: Grand Hall → Modern Wing → Sculpture Garden → Restoration Room → back to Grand Hall.
- Two shortcut corridors cross the center of the loop, connecting the Grand Hall directly to the Sculpture Garden and the Modern Wing directly to the Restoration Room. Each shortcut is 3–4 spaces long.
- Each wing contains 5–6 spaces within its section of the main loop, including 4 Curation Spots, 1 Event Spot, and sometimes a shared border spot with the adjacent wing.

**Movement Dynamics:**
- Players following the main loop pass through every wing naturally, making Movement tasks like M1 (visit all wings) easier but also less suspicious.
- Shortcuts tempt players to skip wings, which is efficient but creates a visible tell — taking a shortcut means you're skipping a wing, which could signal a Neglect task or could just be smart routing.

The Grand Circuit is used for all player counts (3–6). The tight board keeps players in close proximity regardless of count, ensuring movement patterns remain observable.

---

## Player Count Balancing

| Players | Tasks Drawn | Pool Size | Collections | Collection Trigger | Max Turns Per Player | Event Spots | Notes |
|---|---|---|---|---|---|---|---|
| 3 | 9 | 25 | 5 | 3 of 5 completed | 12 | 4 | Intimate — fewer tasks in play relative to pool, more turns to observe. High deduction accuracy expected. |
| 4 | 12 | 30 | 5 | 3 of 5 completed | 11 | 4 | Balanced. Good data-to-player ratio. Default playtesting target. Both triggers fire regularly. |
| 5 | 15 | 30 | 5 | 3 of 5 completed | 10 | 4 | Increased deduction difficulty — more tasks to identify across more players. Guess countdown becomes a more common trigger. |
| 6 | 18 | 30 | 5 | 3 of 5 completed | 9 | 4 | Hardest to read the full table. Perfect deduction is rare and highly rewarded. Guess countdown pressure is highest here. |

### Scaling Levers

- **Pool size** scales with player count to maintain enough unassigned tasks that opponents can't simply eliminate possibilities (at 6 players, 18 of 30 tasks are drawn — 60% — leaving meaningful ambiguity).
- **Collection Trigger threshold** is fixed at 3 of 5 collections across all player counts.
- **Max turns per player** decreases slightly at higher counts to keep total round time manageable, but remains generous enough for 3-task completion.
- **Event Spot density** is fixed at 4 across all player counts on the Grand Circuit layout.

---

## Information Architecture

### Visible to All Players

- The full dossier (public task pool reference).
- The board, all wings, all spots, and all player positions.
- The spinner result each turn.
- Every Curation Card played.
- Every Event Card played and its effect.
- Collection progress for all collections.
- How many collections remain until the Collection Trigger fires.
- Whether the Guess Countdown is active and how many turns remain.
- Which players are holding an Event Card (but not which type).
- Whether a player has submitted guesses this round (but not the content).
- Complete play history for the current round.
- Cumulative scores from prior rounds.

### Visible Only to Each Player

- Their own 3 secret tasks.
- Their own guess submission (once made).
- Catalog results (what they learned about another player's draws).

### Revealed at Round End

- All secret tasks.
- All guess submissions and which guesses were correct.
- Point breakdown for the round.

---

## Open Design Questions

- **Exact space counts:** The Grand Circuit layout defines structure and connectivity, but precise space counts per wing and corridor need tuning through playtesting. The board must be large enough to spread players out but small enough that 3–4 turns of movement data reveals meaningful patterns.
- **Task combination balance:** With 3 tasks per player, certain combinations may be synergistic (two Devotion tasks for the same wing) or contradictory (a Devotion task and a Neglect task for the same collection). Whether to allow, prevent, or weight these combinations needs playtesting.
- **Difficulty calibration:** The category-based difficulty scoring (Easy 1pt / Medium 2pt / Hard 3pt) may need per-task adjustments. Some individual tasks within a category may be significantly easier or harder than others.
- **Tie-breaking:** If two players tie in cumulative score at the end of the match, possible tiebreakers include total correct guesses, total tasks completed, or a sudden-death bonus round.
- **Post-round replay:** A post-round replay feature that highlights each player's key moves and how they connected to their secret task could be satisfying and educational for developing deduction skills.
- **Repeat play and meta-progression:** Whether there's a persistent profile, match history, or unlockable task pool expansions for extended play groups.
