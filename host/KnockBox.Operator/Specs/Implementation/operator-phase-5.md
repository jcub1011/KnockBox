# Phase 5: Testing & Verification

## Context
Robust testing is required, particularly for the mathematical operations, explicit state transitions, and the new Reaction window.

## Requirements
*   **Unit Tests:** Focus on `OperatorGameContext`. Test deck generation boundaries, the scoring algorithm (rounding using MidpointRounding.AwayFromZero, negative numbers, and the divide-by-zero exception).
*   **Integration Tests:** Simulate a multi-player game flow through the `OperatorGameEngine` and FSM. Ensure players transition cleanly from Setup -> Play -> Draw -> Play.
*   **Action & Reaction Testing:** Create targeted integration tests verifying that targeted actions move the game to `ReactionState`, that `Shield` blocks specific actions in that state, and that passing resolves them correctly. Test that `Liability Transfer` correctly redirects point swings when unshielded.

## Deliverables
*   `KnockBox.OperatorTests/Unit/Context/DeckGenerationTests.cs`
*   `KnockBox.OperatorTests/Unit/Context/MathEvaluationTests.cs`
*   `KnockBox.OperatorTests/Integration/FSM/GameFlowTests.cs`
*   `KnockBox.OperatorTests/Integration/FSM/ActionReactionTests.cs`

## Acceptance Criteria
*   Math Evaluation tests achieve 100% coverage on rounding, negative multiplication/division, and the divide-by-zero exception.
*   Game Flow tests successfully simulate a full game loop from `Setup` to `GameOver`.
*   All Action Reaction tests explicitly verify FSM transitions to `ReactionState` and assert expected state mutation vs. nullified state mutation (when Shielded).