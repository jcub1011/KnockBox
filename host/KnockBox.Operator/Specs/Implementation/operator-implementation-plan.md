# Implementation Plan: Operator Game Architecture

## Overview
This document outlines the architecture and implementation plan for the "Operator" game, a mathematical strategy / card brawler game for the KnockBox platform. The game heavily relies on precise decimal point tracking, complex card action resolution, and a strict turn structure.

## Architectural Approach
The game will follow the Finite State Machine (FSM) architecture pattern established in `Codeword`. This ensures server-authoritative logic, thread-safety, and robust state transitions without direct, ad-hoc state mutations.

## Core Components
1.  **Models**: Enums for Card types (Number, Operator, Action), a `Card` base class/record, and `OperatorPlayerState`.
2.  **State Management**: `OperatorGameState` holding the deck, discard pile, players, and turn manager. `OperatorGameContext` holding FSM-agnostic logic (deck generation, mathematical evaluation).
3.  **FSM Engine**: `OperatorGameEngine` acting as the UI-facing gateway, dispatching `OperatorCommand` payloads to the underlying FSM.
4.  **FSM States**:
    *   `SetupState`: Waiting for players to choose 10.0 or -10.0.
    *   `PlayPhaseState`: Active player must play at least 1 card.
    *   `ReactionState`: Suspends the turn to allow a targeted player to respond with a Shield or pass.
    *   `DrawPhaseState`: Active player draws up to 3 cards.
    *   `GameOverState`: Winner calculation.

## Phased Implementation
The implementation is broken down into 5 phases. Please see the following phase documents for detailed context, deliverables, and acceptance criteria:
1.  **Phase 1: Core Models & FSM Scaffolding**
2.  **Phase 2: Context Logic & Engine Base**
3.  **Phase 3: FSM States Implementation**
4.  **Phase 4: Action Card Resolution**
5.  **Phase 5: Testing & Verification**