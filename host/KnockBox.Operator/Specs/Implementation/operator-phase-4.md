# Phase 4: Action Card Resolution

## Context
Action cards are the core of Operator's chaos. This phase implements the resolution logic for all specific Action Cards, explicitly integrated with the new `ReactionState` for reactionary cards like `Shield`.

## Requirements
*   **Action Logic in Context:** Implement handlers for each action card within `OperatorGameContext` or within the state processing logic.
    *   `Liability Transfer`: Redirects the current turn's score mutation to a target player.
    *   `Cook the Books`: Handled alongside a Number card to divide current score.
    *   `Comp`: Sets operator to `+` if negative, `-` if positive.
    *   `Steal`: Takes 1 random card from opponent.
    *   `Hot Potato`: Gives a number card to opponent.
    *   `Flash Flood`: Forces target to draw 2 immediately (can exceed max hand size temporarily).
    *   `Hostile Takeover`: Swaps `activeOperator` with target.
    *   `Audit`: Locks operator for 1 round.
    *   `Market Crash`: Replaces everyone's operator with `/`.
*   **Reactionary System Implementation:**
    *   Targeted actions (`Liability Transfer`, `Steal`, `Hot Potato`, `Flash Flood`, `Hostile Takeover`, `Audit`) must trigger a transition to `ReactionState` rather than mutating state immediately.
    *   The `ReactionState` governs the interrupt window. When the target plays a `Shield` (`PlayReactionCommand`), the Shield is discarded from their hand, the pending action is cleared, and the effect does not resolve.
    *   If the target issues a `PassReactionCommand`, the original pending action's logic is fully executed.

## Deliverables
*   `KnockBox.Operator/Services/Logic/FSM/OperatorGameContext.cs` (Added Action Resolution methods)
*   `KnockBox.Operator/Services/Logic/FSM/States/PlayPhaseState.cs` (Updated to queue Action cards into ReactionState)
*   `KnockBox.Operator/Services/Logic/FSM/States/ReactionState.cs` (Implemented action resolution execution on pass)

## Acceptance Criteria
*   All 10 Action Cards correctly mutate the game state according to the GDD when resolved.
*   A player can successfully play a `Shield` in `ReactionState` to block a targeted action (e.g., `Liability Transfer` or `Steal`), and the incoming effect is cleanly nullified without mutating the target's primary state.
*   Passing in `ReactionState` correctly applies the pending action.