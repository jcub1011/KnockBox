# Phase 3: FSM States Implementation

## Context
This phase builds the actual flow of the game by implementing the individual FSM states. This includes enforcing the turn structure, concatenation rules, and the explicit Reaction state for targeted actions.

## Requirements
*   **SetupState:** Players must submit their starting choice (+10.0 or -10.0). Once all players submit, deal 5 cards to each player and transition to `PlayPhaseState`.
*   **PlayPhaseState:** Active player must play at least 1 card.
    *   Validate multi-card plays (e.g., Concatenation: playing `9` and `9` means `99`). The order of `CardId`s in the `PlayCardsCommand` explicitly defines the concatenation order.
    *   If a targeted Action card is played (e.g., Steal, Liability Transfer), the state is mutated to store the `PendingActionCommand` and `ReactionTargetPlayerId`, and the FSM transitions to `ReactionState`.
    *   If no targeted Action is played, resolve the basic state mutation and transition to `DrawPhaseState`.
    *   If the player only has Shield cards, allow a skip.
*   **ReactionState:** Suspends the active turn. Only accepts commands from `ReactionTargetPlayerId`.
    *   Accepts `PlayReactionCommand` (must be a Shield). If valid, consumes the Shield, nullifies the `PendingActionCommand`, and transitions to `DrawPhaseState` for the original active player.
    *   Accepts `PassReactionCommand`. If passed, resolves the `PendingActionCommand` fully and transitions to `DrawPhaseState`.
*   **DrawPhaseState:** Active player draws up to 3 cards to replenish their hand. Strict maximum hand size of 5 cards.
    *   Check win condition: If the deck is empty AND all players have exhausted their playable non-Shield moves, transition to `GameOverState`.
    *   Otherwise, advance the `TurnManager` to the next player and transition back to `PlayPhaseState`.
*   **GameOverState:** Evaluate all player scores. Find the absolute value closest to `0.0`. In a tie, use the `ScoreTimestamp` (earliest wins).

## Deliverables
*   `KnockBox.Operator/Services/Logic/FSM/Commands/` (Create `SubmitSetupChoiceCommand`, `PlayCardsCommand`, `DrawCardsCommand`, `SkipTurnCommand`, `PlayReactionCommand`, `PassReactionCommand`)
*   `KnockBox.Operator/Services/Logic/FSM/States/SetupState.cs`
*   `KnockBox.Operator/Services/Logic/FSM/States/PlayPhaseState.cs`
*   `KnockBox.Operator/Services/Logic/FSM/States/ReactionState.cs`
*   `KnockBox.Operator/Services/Logic/FSM/States/DrawPhaseState.cs`
*   `KnockBox.Operator/Services/Logic/FSM/States/GameOverState.cs`

## Acceptance Criteria
*   The game cannot advance from `SetupState` until all players have made their choice.
*   `PlayPhaseState` correctly concatenates numbers based on array order and rejects a turn if no cards are played (unless skipping with a hand full of Shields).
*   Playing a targeted action successfully routes the FSM to `ReactionState`.
*   `ReactionState` correctly handles both a Shield play and a Pass, resuming to `DrawPhaseState` in both cases.
*   `DrawPhaseState` correctly enforces the 5-card maximum limit (e.g., if hand has 4 cards, drawing 3 only yields 1).
*   `GameOverState` accurately identifies the winner based on absolute distance to 0.0 and timestamps.