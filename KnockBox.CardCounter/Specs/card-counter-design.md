# Card Counter

## Objective

End the game with a balance as close to 0 as possible. Negative balances are allowed.

## Setup

- **Players:** 2–? (to be determined via playtesting)
- **Buy-In (Starting Balance):** Roll a 6-sided die and multiply the result by 8. The player chooses whether their starting balance is positive or negative.
- **Main Deck:** 52 cards containing only number cards and operator cards (deck size, card counts, and ratios are placeholder values to be adjusted via playtesting). The deck is shuffled once and divided into subsets called shoes for each round.
- **Action Deck:** A separate deck of action cards, not part of the main deck.
- **Turn Order:** Clockwise.

> **Playtesting Note:** The following values are placeholders and subject to change: deck size (52), number-to-operator ratio (4:1), add/subtract-to-multiply/divide ratio (4:1), action cards dealt per round (3), action card hand limit (6), and total passes per game.

---

## Card Types

### Number Cards (0–9)

Number cards form the player's **pot** through concatenation. Drawing a 3, then a 7, then a 1 gives a pot value of 371.

**Leading Zeros:** Leading zeros are ignored when calculating the pot's value (001 = 1) but are preserved in the pot. This matters because a "Turn The Table" can move trailing zeros to the front and vice versa (e.g., a pot of [0, 0, 5] has a value of 5, but after a flip becomes [5, 0, 0] with a value of 500).

### Operator Cards (+, −, ×, ÷)

Addition and subtraction are common. Multiplication and division are rare.

When a player draws an operator card:

1. If the player's pot is empty, the operator is a **no-op** (nothing happens).
2. Otherwise, the player's new balance is calculated as: **New Balance = Current Balance [Operator] Pot Value**. The pot is then cleared.

**Rounding:** Operations that result in decimals are rounded to the nearest integer.

**Division by Zero:** If a player's pot value is 0 when a division operator is drawn, one of the following events occurs at random:

- Player gains an extra pass.
- Player loses a pass.
- Player gains a random action card (subject to hand limit overflow rules).
- Player loses a random action card (no-op if the player has no action cards).

### Action Cards

Action cards come from a separate deck and are **not** part of the main deck. Players are dealt 3 action cards at the start of every round. A player may hold a maximum of 6 action cards at any time. If a player exceeds this limit, they may view all their cards before choosing which to discard down to 6.

**General Rules:**

- Action cards are played from hand **before** the player draws for the turn.
- A player may play multiple action cards per turn unless a card states otherwise.
- Playing an action card **discards it**, even if it is blocked.

#### Action Card List

| Card | Effect |
|---|---|
| **Feeling Lucky?** (Force Draw) | Forces the next player to draw a card. That player may respond by playing their own "Feeling Lucky?" to pass the force to the next player in turn order, forming a chain. If any player in the chain plays "Comp'd," the player before them in the chain must still draw or pass. The round resumes from the player who initially played "Feeling Lucky?" This card **does not** end your turn. Forced players do not have their normal turn skipped. |
| **Make My Luck** (Alter the Future) | View the top 3 cards of the current shoe and reorder them in any order. |
| **Skim** (Swap a Digit) | Swap a single digit from your pot with a single digit from a different player's pot. Digits remain in their respective positions. *(Example: Player A swaps their digit in position 1 with Player B's digit in position 2. Player A's position 1 now holds Player B's old digit, and Player B's position 2 now holds Player A's old digit.)* Blockable. |
| **Burn** (Discard) | Discard the top card of the current shoe. The discarded card is revealed to all players. |
| **Turn The Table** (Flip) | Reverse the digit order of a targeted player's pot. Blockable. |
| **Comp'd** (Shield) | Negate the effect of any action card that targets you. Cannot be used against cards that do not target you (e.g., Burn). |
| **Not My Money** (Redirect) | Played when you draw an operator card. Redirect the operator to a different player, applying it to **that player's** pot and balance instead. If blocked, the operator applies to the drawing player as normal. Can be played against players with an empty pot (resulting in a no-op for the target). Blockable. |
| **Launder** (Swap Pots) | Swap your entire pot with a different player's pot. Can be used when either or both pots are empty. Blockable. |
| **Tilt** (Redistribute) | Combine all number cards from every player's pot into a single pool, shuffle them, and redistribute them evenly. Any extra cards (i.e., total cards mod player count) are dealt one at a time in turn order starting from the player who played Tilt. |
| **Hedge Your Bet** (Convert Next Draw) | Convert the next card drawn from the shoe into an operator: **+** if your balance is negative, **−** if your balance is zero or positive. The conversion applies to the very next draw by any player, regardless of whose turn it is. This card does **not** automatically draw — you may still play other cards (such as Make My Luck) before drawing. Only playable when the shoe is not empty. |
| **Let It Ride** (Extra Turn) | Grant yourself one additional turn after your current turn ends. This card **stacks**: playing two Let It Ride cards in a single turn queues two extra turns. Extra turns are consumed one at a time after each draw, before play advances to the next player. |

#### Interaction Example

> Player A draws an operator and plays **Not My Money**, redirecting it to Player B. Player B plays **Comp'd** to block. The operator bounces back to Player A and resolves using Player A's pot. Both cards are discarded.

---

## Round Structure

### Shoes

The shuffled main deck is divided into subsets called **shoes**. Each shoe is dealt a random number of cards between 12 and 20. If fewer than 12 cards remain for the final shoe, those remaining cards form the last shoe as-is.

### Card Visibility

At the start of each shoe, players are shown the **quantity** of each card type in the shoe (but not their order). The remaining card quantities are updated live as cards are drawn throughout the round.

### Turn Structure

On each turn, a player may:

1. **Play action cards** (optional, before drawing).
2. **Fold** (optional, consumes a pass, does not end the turn).
3. **Draw** the top card of the current shoe **or pass**.

### Passing

Players have a limited number of passes for the **entire game** (quantity to be determined via playtesting). Passing skips the player's draw for that turn and consumes one pass.

### Folding

From the same pass pool, the player can opt to fold their pot. This consumes a pass but discards their pot. Their turn does not end with this action.

### Transparency

- All player **balances** and **pots** are visible at all times.
- Action cards in hand are **hidden**.
- The only unknown information is the **order** of the remaining cards in the current shoe.

---

## Game End

The game ends when all cards in the main deck have been drawn across all shoes.

**Winning:** The player with the balance closest to 0 wins.

**Tiebreaker:**

1. The tied player with the most remaining passes wins.
2. If still tied, the winner is determined by coin flip.