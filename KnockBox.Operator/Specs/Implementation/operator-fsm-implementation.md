# Implementation Plan: Operator Game

## Background & Motivation
The user requested the implementation of "Operator", a mathematical strategy / card brawler game for the KnockBox platform. Based on the provided Game Design Document (GDD), Operator requires precise decimal point tracking, complex card action resolution, and a strict turn structure. The game should follow the Finite State Machine (FSM) architecture pattern established in `Codeword` to ensure server-authoritative logic, thread safety, and maintainable state transitions.

## Scope & Impact
*   **Target Project:** `KnockBox.Operator` (Class Library) and its corresponding test project `KnockBox.OperatorTests`.
*   **Architecture:** Will introduce the FSM pattern (`AbstractGameEngine`, `AbstractGameState`, FSM States and Commands) specifically tailored for Operator's turn-based mechanics and real-time Action cards.
*   **Math Precision:** Strict adherence to C# `decimal` types and server-side evaluation.

## Proposed Solution: FSM Architecture Mapping

### 1. Data Models & Enums
*   **Cards:** A robust hierarchy or enum-driven structure representing Number Cards (0-9), Operator Cards (+, -, *, /), and Action Cards (Shield, Comp, etc.).
*   **`OperatorPlayerState`:** Tracks `CurrentPoints` (decimal), `ActiveOperator` (enum), `Hand` (List of Cards), active buffs/debuffs (`IsAudited`, `HasLiabilityTransfer`), and `ScoreTimestamp` (DateTimeOffset for tie-breakers).
*   **`OperatorGamePhase`:** Enum representing the high-level game state (`Setup`, `Play`, `Draw`, `GameOver`).

### 2. State & Context
*   **`OperatorGameState`:** Inherits `AbstractGameState`. Holds a `ConcurrentDictionary<string, OperatorPlayerState>`, the main `Deck`, `DiscardPile`, `Phase`, and the `TurnManager`.
*   **`OperatorGameContext`:** Holds FSM-agnostic helper methods: shuffling/generating decks based on player count, drawing cards, concatenating numbers, applying mathematical operations, and executing specific Action card logic.

### 3. FSM Commands
Commands act as the strongly-typed payloads dispatched from the UI to the Engine:
*   `OperatorCommand` (Abstract Base)
*   `SubmitStartingChoiceCommand(decimal Choice)` (+10.0 or -10.0)
*   `PlayCardsCommand(List<CardId> Cards, string? TargetPlayerId)`
*   `SkipTurnCommand()` (Only valid if hand is entirely Shields)
*   `DrawCardsCommand()`
*   `PlayReactionCommand(CardId CardId)` (For playing Shields out of turn)

### 4. FSM States
The states define the strict rules of engagement:
*   **`SetupState`:** Waits for all players to lock in their starting value. Once all have chosen, deals the initial 5 cards and transitions to `PlayPhaseState`.
*   **`PlayPhaseState`:** Enforces the rule that the active player must play at least 1 card. Handles validation of valid plays (Concatenation, Operators, Actions). If a targeted action is played, transitions to `ReactionState`.
*   **`ReactionState`:** Suspends the turn to allow a targeted player to respond with a Shield or pass.
*   **`DrawPhaseState`:** Allows the active player to draw up to 3 cards (capped at a hand size of 5). Checks if the game-ending condition is met (Empty deck AND no valid moves). If game continues, advances the `TurnManager` and loops back to `PlayPhaseState`.
*   **`GameOverState`:** Determines the winner. Calculates absolute distances to 0.0 and checks the `ScoreTimestamp` for tie-breakers.

### 5. Action Resolution & The Reaction State
Because Operator features reactionary cards like **Shield** and targeted actions, the FSM architecture uses an explicit `ReactionState`. When a targeted action is played (e.g., `Hostile Takeover`), the FSM transitions to `ReactionState`, suspending the active turn. The target player must then either play a Shield or pass before the action resolves and the turn proceeds to the draw phase.

### 6. Game Engine (`OperatorGameEngine`)
*   Inherits `AbstractGameEngine` with `minPlayerCount = 2`, `maxPlayerCount = 8`.
*   Provides standard UI-facing wrapper methods that wrap `state.Execute(...)` and forward commands into the FSM.

## Implementation Plan

### Phase 1: Core Models & FSM Scaffolding
1.  Define `Card` records, Enums, and `OperatorPlayerState` in `KnockBox.Operator`.
2.  Create `OperatorGameState`, `OperatorGamePhase`, and `OperatorGameContext`.
3.  Define the base FSM interfaces `IOperatorGameState` and `OperatorCommand`.

### Phase 2: Context Logic & Math
1.  Implement `OperatorGameContext.GenerateDeck(playerCount)` to handle the 80-card deck scaling (2:1:1 ratio).
2.  Implement the scoring formula in the Context: `New Score = round(Current Points [Active Operator] Number Value, 1)`. Specifically handle the divide-by-zero fallback to `0.0` and operator reversion to `+`. Use `MidpointRounding.AwayFromZero`.

### Phase 3: FSM States Implementation
1.  Build `SetupState` to handle initialization.
2.  Build `PlayPhaseState` ensuring single-commit multi-card drops are valid (e.g., concatenating `9` and `9` into `99`).
3.  Build `ReactionState` to handle the interrupt window for targeted actions.
4.  Build `DrawPhaseState` enforcing max hand bounds.
5.  Build `GameOverState` applying the closest-to-zero and timestamp algorithms.

### Phase 4: Engine Integration & Edge Cases
1.  Implement `OperatorGameEngine.StartAsync` to wire up the FSM.
2.  Implement targeted Action Card logic (Liability Transfer, Market Crash, Audit).

### Phase 5: Testing
1.  Unit test the mathematical evaluation heavily in `KnockBox.OperatorTests`.
2.  Integration test the FSM flow to ensure players cannot draw out of sequence or bypass the mandatory play rule.

## Verification
*   **Deck Scaling:** Verify a 5-player game generates 160 cards.
*   **Precision:** Assert `currentPoints = 1.3`, `operator = *`, `value = 2` results in `2.6`.
*   **Flow Control:** Verify players without valid moves (only Shields) can skip their turn cleanly.

## Migration & Rollback
*   The system uses purely transient, in-memory state. Rollback is as simple as reverting the git branch containing the newly added project and DI container registrations. No database migration is necessary.