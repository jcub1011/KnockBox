# Phase 1: Core Models & FSM Scaffolding

## Context
This phase establishes the foundational data structures and FSM scaffolding required for the Operator game. It mirrors the `ConsultTheCard` pattern but replaces the phase enums and state data with Operator-specific concepts, while setting up the specific properties needed for reactionary cards.

## Requirements
*   Define the `Card` data structure supporting Numbers (0-9), Operators (+, -, *, /), and Actions (Shield, Liability Transfer, etc.).
*   Create `OperatorPlayerState` to track a player's `CurrentPoints` (decimal), `ActiveOperator`, `Hand` (List of cards), and buffs/debuffs (`IsAudited`, `HasLiabilityTransfer`). Also needs a `ScoreTimestamp` (DateTimeOffset) for tie-breaking.
*   Create `OperatorGameState` inheriting `AbstractGameState` holding the main `Deck`, `DiscardPile`, `Phase`, and `TurnManager`. It must also contain properties for resolving targeted actions: `PendingActionCommand` (the action waiting to resolve) and `ReactionTargetPlayerId` (the player who must respond).
*   Create `OperatorGameContext` to hold the FSM and contextual game data.
*   Define the FSM interfaces `IOperatorGameState` and base record `OperatorCommand`.

## Deliverables
*   `KnockBox.Operator/Models/Card.cs` (or equivalent enum/records)
*   `KnockBox.Operator/Models/OperatorPlayerState.cs`
*   `KnockBox.Operator/Models/OperatorGamePhase.cs` (Enum: Setup, Play, Reaction, Draw, GameOver)
*   `KnockBox.Operator/Services/State/OperatorGameState.cs`
*   `KnockBox.Operator/Services/Logic/FSM/OperatorGameContext.cs`
*   `KnockBox.Operator/Services/Logic/FSM/OperatorCommand.cs`
*   `KnockBox.Operator/Services/Logic/FSM/IOperatorGameState.cs`

## Acceptance Criteria
*   The project compiles successfully.
*   `OperatorGameState` inherits `AbstractGameState` and implements `IFsmContextGameState<OperatorGameContext>`.
*   `OperatorGameState` correctly includes fields for `PendingActionCommand` and `ReactionTargetPlayerId`.
*   `OperatorPlayerState` uses `decimal` for `CurrentPoints`.