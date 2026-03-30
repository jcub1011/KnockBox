# Consult The Card — Game Design Document

**Version:** 1.0  
**Date:** March 30, 2026  
**Genre:** Social Deduction / Bluffing / Word Association  
**Players:** 4–8  
**Age:** 10+  
**Play Time:** ~20 minutes per round

---

## 1. Concept Overview

**Consult The Card** is a social deduction party game built around bluffing, word association, and hidden identities. Each player receives a card containing a secret word. Some players share the same word; others have been given a different one — but nobody knows which side they're on. Through careful clue-giving, debate, and deduction, players must figure out who belongs and who is the impostor, all while trying not to expose themselves.

**Tagline:** *"Trust no one. Not even yourself."*

---

## 2. Core Experience Goals

- **Accessible:** Simple enough that anyone can learn in under two minutes, with no prior gaming experience required.
- **Social:** The game is driven by conversation, reading people, and reacting to clues — not by complex strategy or resource management.
- **Replayable:** A large word bank and shifting roles ensure no two rounds feel the same.
- **Scalable Fun:** Enjoyable at 4 players, but truly shines with 6–8.

---

## 3. Components

| Component | Description |
|-----------|-------------|
| Role Cards | One per player. Each card contains a grid of secret words mapped to round numbers, allowing many rounds without reshuffling. |
| Mission Cards | Define which word pair is in play for the current round and which card holders are Insiders vs. Agents. |
| Power Cards | Optional modifiers that assign special abilities or restrictions to individual players for a round. |

---

## 4. Roles

### 4.1 Agents (Majority Team)
Agents all share the **same secret word**. Their goal is to identify and eliminate the Insiders and the Informant through clue-giving and voting. They win when all Insiders and the Informant have been eliminated.

### 4.2 Insiders (Minority Team)
Insiders have a **different secret word** — one that is thematically related but distinct. They must blend in with the Agents by giving convincing clues, even though their word is different. They win if they survive to the end of the game without being eliminated.

### 4.3 The Informant (Solo Role — 5+ Players)
The Informant has **no word at all**. They must fake their way through every clue round using only context from other players' clues. The Informant wins by either surviving undetected until the game ends, or correctly guessing the Agents' secret word when voted out. The Informant only gets one guess attempt, and it can only be made at the moment they are voted out — not at any other time during the game.

**Key Twist:** Players do not know their own role at the start. You know your word (or lack of one), but you don't know whether your word belongs to the majority or the minority. You discover your allegiance through gameplay.

---

## 5. Game Flow

### 5.1 Setup

1. Select a Mission Card. This determines the two secret words for the round and assigns roles based on each player's Role Card number.
2. Each player looks at their Role Card and finds the word corresponding to the current round number. Some players will see Word A, others Word B, and one (if 5+ players) will see a blank or special symbol indicating they are the Informant.
3. Optionally deal one Power Card to each player (or a subset).

### 5.2 Clue Phase

Starting with a randomly chosen player and proceeding clockwise:

- Each player says **one word** (a clue) that relates to their secret word.
- Clues must be a single word. No phrases, sentences, or direct synonyms of the secret word itself.
- Players may not pass.

**Strategic tension:** You want your clue to prove you belong — but giving too obvious a clue helps the Insiders blend in, and giving too obscure a clue makes you look suspicious.

### 5.3 Discussion Phase

After all players have given a clue, open discussion begins. Players debate, accuse, defend, and question each other. There is no time limit by default, but groups may choose to use a timer (recommended: 2–3 minutes).

### 5.4 Vote Phase

All players simultaneously point at the player they want to eliminate (or use a countdown method). The player with the most votes is eliminated. In the case of a tie, no one is eliminated and a new Clue Phase begins.

### 5.5 Reveal

The eliminated player's role is **not** revealed to the group. The only exception is the Informant: if the eliminated player is the Informant, their identity is revealed through the guess mechanic (they are prompted to guess the Agents' word — see Section 6). For all other roles, the eliminated player's allegiance remains hidden until the game ends.

### 5.6 Repeat

The remaining players begin a new Clue Phase with a **new clue** (you cannot reuse a previous clue). The game continues until one of the end conditions is met.

---

## 6. Win Conditions

| Role | Win Condition | Priority |
|------|---------------|----------|
| **Informant** | Survive until the game ends **or** correctly guess the Agents' secret word when voted out. | Highest |
| **Insiders** | Have at least one Insider alive when the game ends (by vote to end or 2 players remaining). | Middle |
| **Agents** | Be the only role remaining when the game ends. | Lowest |

**Win Priority:** When the game ends, wins are evaluated in order: Informant → Insider → Agent. If the Informant is alive, the Informant wins. Otherwise, if any Insider is alive, Insiders win. Otherwise, Agents win.

**Game End Triggers:** The game ends when (1) only two players remain, (2) a majority of alive players vote to end the game, or (3) a voted-out Informant correctly guesses the Agents' word. The game does **not** auto-end when all Insiders or the Informant are eliminated — doing so would reveal information about which roles are still in play.

---

## 7. Power Cards (Optional Module) [DO NOT IMPLEMENT - MAY BE INCLUDED IN FUTURE RELEASE]

Power Cards add variety and unpredictability. Shuffle and deal one to each player at the start of a round. Examples:

| Power Card | Effect |
|------------|--------|
| **The Mute** | You skip the Clue Phase this round but get two votes during the Vote Phase. |
| **The Investigator** | After the Vote Phase, you may privately peek at one eliminated player's Role Card. |
| **The Deflector** | If you would be eliminated, redirect the elimination to the player with the second-most votes instead. |
| **The Double Agent** | Your win condition flips — if you are an Agent, you now win with the Insiders, and vice versa. You do not learn this until you consult the card. |

---

## 8. Scoring

For groups that want to play multiple rounds and track a winner:

| Outcome | Points |
|---------|--------|
| Survived the round (any role) | +2 |
| On the winning team | +1 |
| Informant correctly guesses the word | +3 |
| Incorrectly accused (voted for someone who turned out to be an Agent) | −1 |

Play a set number of rounds (recommended: 5–7) and tally scores. Highest total wins.

---

## 9. Player Scaling

| Player Count | Agents | Insiders | Informant | Notes |
|--------------|--------|----------|-----------|-------|
| 4 | 3 | 1 | — | Tight and tense. Every clue matters. |
| 5 | 3 | 1 | 1 | Informant enters play. |
| 6 | 4 | 1 | 1 | Sweet spot for balanced games. |
| 7 | 4 | 2 | 1 | Two Insiders can cover for each other. |
| 8 | 5 | 2 | 1 | Maximum chaos. Highly recommended. |

---

## 10. Word Design Philosophy

The two secret words on each Mission Card should be **thematically adjacent but distinct**. This ensures Insiders can plausibly bluff, while still leaving room for Agents to catch inconsistencies.

**Examples:**

| Word A (Agents) | Word B (Insiders) |
|-----------------|-------------------|
| Ocean | Lake |
| Guitar | Violin |
| Castle | Fortress |
| Sunrise | Sunset |
| Astronaut | Pilot |

Words that are too similar (e.g., "car" / "automobile") make the game impossible for Agents. Words that are too different (e.g., "banana" / "algebra") make it trivial. The sweet spot is words that share some associations but diverge on others.

---

## 11. Design Pillars

1. **You Are the Mechanic.** The game's depth comes from human interaction, not from cards or tokens. The components facilitate; the players create.
2. **Doubt Is the Engine.** The best moments happen when no one is sure — not even of their own allegiance.
3. **Speed Over Complexity.** Rounds should be fast. If a group finishes a round in under 10 minutes, the design is working.
4. **Everyone Participates.** There is no downtime. Even eliminated players enjoy watching the remaining chaos unfold.

---

## 12. Appendix: Quick-Start Rules

1. **Get your card.** Look at your secret word for this round. Don't share it.
2. **Give a clue.** Say one word that relates to your secret word.
3. **Discuss.** Talk it out. Who's bluffing? Who belongs?
4. **Vote.** Point at the player you want to eliminate.
5. **Reveal.** The eliminated player consults their card and announces their role.
6. **Repeat** until the Agents have won — or the Insiders have slipped through.

*Remember: you don't know which team you're on. Consult the card.*
