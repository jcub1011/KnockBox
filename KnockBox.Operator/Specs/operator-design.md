# Game Design Document: Operator

**Project Type:** Blazor Server Application  
**Genre:** Mathematical Strategy / Card Brawler  
**Objective:** Be the player with a score closest to **0.0** when the game ends.  
**Players:** 2-8+ (Scales dynamically with deck count)  

---

## 1. Game Overview
**Operator** is a highly competitive, fast-paced digital card game where players manipulate their own scores and sabotage others using arithmetic and strategic action cards. The game focuses on "tug-of-war" mechanics, chaotic point swings, and brutal "hot potato" endgames where players must navigate a shifting landscape of operators and numerical "bursts."

---

## 2. Core Mechanics

### 2.1 The Score Formula
Players maintain a `currentPoints` value and an `activeOperator`. All calculations are processed on the server to ensure decimal precision.

$$New Score = round(Current Points [Active Operator] Number Value, 1)$$

* **Decimal Precision:** Rounded to the nearest tenth (0.1).
* **Divide by Zero:** If a player uses the `/` operator with a 0, their score immediately becomes **0.0**, but their `activeOperator` reverts to `+`.
* **Zero Strategy:** Reaching 0.0 is highly advantageous for tie-breakers, but leaves the player vulnerable. Because players *must* play on their turn, sitting at a perfect score often forces players to alter their own score if opponents shield themselves.

### 2.2 Setup
* **Deck Size:** 80 Cards per 4 players (1 deck for 2-4 players, 2 decks for 5-8 players, etc.).
* **Hand Size:** 5 Cards.
* **Starting State:**
    * **Points:** Players choose to start at **10.0** or **-10.0**.
    * **Operator:** Matching choice (`+` for 10.0, `-` for -10.0).

### 2.3 Turn Structure
1.  **Play Phase:** A player **must** play at least 1 card. They may play as many cards as they wish in a single "Commit."
    * *Exception:* If a player’s hand consists **only of Shield cards**, they may skip their turn.
2.  **Draw Phase:** Player draws up to **3 cards** to replenish their hand, up to a strict maximum hand size of **5 cards**.
3.  **End State:** The game ends when the deck is empty and all players have exhausted their playable cards (or only have Shields remaining).

---

## 3. Deck Composition (80 Cards per Base Deck)
The base deck is tightly balanced on a **2:1:1** ratio (50% Numbers, 25% Operators, 25% Actions). For every 4 players, an additional 80-card base deck is shuffled in.

### 3.1 Number Cards (40 Cards)
Lower numbers are rare, making the elusive **0** and **1** high-value strategic assets.

| Value | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | Total |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Count** | 2 | 2 | 3 | 3 | 4 | 4 | 5 | 5 | 6 | 6 | **40** |

* **Concatenation:** Players may play multiple number cards simultaneously to create a multi-digit value (e.g., `9` + `9` = `99`). This value is applied as a single transaction, allowing for massive point swings.

### 3.2 Operator Cards (20 Cards)
* **Primary (+ / -):** 8 cards each (16 total).
* **Advanced (* / /):** 2 cards each (4 total).
* **Usage:** Played on any player to replace their `activeOperator`.

### 3.3 Action Cards (20 Cards)
Actions provide utility, defense, and the ability to redirect massive concatenations.

| Card | Target | Effect | Count |
| :--- | :--- | :--- | :--- |
| **Shield** | Self | Purely reactive interrupt. Blocks the effect of any card targeting you. (No duration; consumed on use). | 4 |
| **Liability Transfer** | Any | For this turn, your Number cards (and Concatenations) are applied to the target instead of yourself. | 3 |
| **Cook the Books** | Self | Pair this with a Number card from your hand to instantly divide your current score by that number. | 2 |
| **Comp** | Self | Sets operator to `+` if score is negative, `-` if positive. | 2 |
| **Steal** | Opponent | Take one random card from an opponent's hand. | 2 |
| **Hot Potato** | Opponent | Give a number card from your hand to an opponent. (Redirectable). | 2 |
| **Flash Flood** | Any | Force target to draw 2 cards immediately. | 2 |
| **Hostile Takeover**| Any | Swap your `activeOperator` with another player. | 1 |
| **Audit** | Any | Protects target's `activeOperator` from changes for 1 round. | 1 |
| **Market Crash** | All | Replaces every player's operator with `/`. | 1 |

---

## 4. Winning Conditions
1.  **The Goal:** The player with the absolute value closest to **0.0** when all valid plays are exhausted wins.
2.  **Tie-Breaker:** The player who **reached their final score first** (timestamped by server) is the winner. This heavily rewards early aggressive pushes toward zero, provided the player can survive the resulting target on their back.

---

## 5. Blazor Implementation Guidelines
**Decimal Handling:** Strict usage of the C# `decimal` type is required for all mathematical operations to guarantee financial-level precision and avoid floating-point inaccuracies.