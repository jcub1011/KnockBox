# Phase 2: Context Logic & Engine Base

## Context
With the models in place, this phase implements the raw game logic (FSM-agnostic) within `OperatorGameContext` and sets up the Blazor-facing `OperatorGameEngine`. The math logic is critical: it must use C# `decimal`, strictly handle standard rounding, and account for the divide-by-zero rule.

## Requirements
*   **Deck Generation:** Implement `OperatorGameContext.GenerateDeck(int playerCount)`.
    *   1 Base Deck per 4 players (80 cards per base deck). 2-4 players = 1 deck, 5-8 players = 2 decks.
    *   Base Deck Composition: 40 Numbers (0x2, 1x2, 2x3, 3x3, 4x4, 5x4, 6x5, 7x5, 8x6, 9x6), 20 Operators (8 `+`, 8 `-`, 2 `*`, 2 `/`), 20 Actions (4 Shield, 3 Liability Transfer, 2 Cook the Books, 2 Comp, 2 Steal, 2 Hot Potato, 2 Flash Flood, 1 Hostile Takeover, 1 Audit, 1 Market Crash).
*   **Math Evaluation:** Implement `CalculateNewScore(decimal currentPoints, Operator op, decimal value)`.
    *   Rounding: Round to nearest tenth (0.1) using `MidpointRounding.AwayFromZero` to eliminate ambiguity.
    *   Divide by Zero Rule: If `op` is `/` and `value` is `0`, result is `0.0` and operator reverts to `+`.
*   **Engine Integration:** Create `OperatorGameEngine` inheriting `AbstractGameEngine`. Implement `CreateStateAsync` and `StartAsync` to initialize the FSM and deal starting cards. Provide a stub UI-facing method for dispatching FSM commands (e.g., `ProcessCommand`).

## Deliverables
*   `KnockBox.Operator/Services/Logic/FSM/OperatorGameContext.cs` (Updated with logic)
*   `KnockBox.Operator/Services/Logic/OperatorGameEngine.cs`

## Acceptance Criteria
*   `GenerateDeck(5)` produces 160 cards.
*   `CalculateNewScore` strictly enforces the 0.1 rounding (AwayFromZero) and handles division by zero perfectly.
*   `StartAsync` initializes the `OperatorGameContext` and transitions the FSM to `SetupState` (which can be a stub for now).