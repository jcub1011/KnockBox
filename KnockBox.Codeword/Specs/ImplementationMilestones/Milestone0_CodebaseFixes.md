# Milestone 0: Codebase Fixes

## Objective
Fix pre-existing naming issues in the codebase before building the new game.

---

## Action Items

### 0.1 Rename Project Directory and References
- Rename `Knockbox.Codeword` folder to `KnockBox.Codeword` to match naming conventions (`KnockBox.CardCounter`, `KnockBox.DiceSimulator`, etc.)
- Update `KnockBox.slnx` to reference the renamed project path

### 0.2 Fix `IFininteStateMachine` Typo
Rename `IFininteStateMachine` to `IFiniteStateMachine` in the following files:
- `KnockBox.Core/Services/State/Games/Shared/IFiniteStateMachine.cs` (interface name)
- `KnockBox.Core/Services/State/Games/Shared/FiniteStateMachine.cs` (implementation references)
- `KnockBox.CardCounter/Services/Logic/Games/CardCounter/FSM/CardCounterGameContext.cs` (property type)
- `KnockBox.DrawnToDress/Services/Logic/Games/DrawnToDress/DrawnToDressGameContext.cs` (property type)

---

## Acceptance Criteria
- [x] Project folder is named `KnockBox.Codeword` (capital K, capital B)
- [x] `KnockBox.slnx` references the renamed project correctly
- [ ] `IFiniteStateMachine` is correctly spelled across all files listed above
- [ ] `dotnet build` succeeds with zero errors after all renames
- [ ] Existing CardCounter and DrawnToDress tests still pass
