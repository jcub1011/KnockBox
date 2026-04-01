# Milestone 6: Integration

## Objective
Wire the new game engine into the existing KnockBox application so that ConsultTheCard lobbies can be created and joined.

---

## Action Items

### 6.1 Register Game Type
**File:** `KnockBox/Services/Navigation/Games/GameTypes.cs`
- Add `ConsultTheCard` enum variant
- Add `[Description("Consult The Card")]` attribute
- Add `[NavigationString("consult-the-card")]` attribute

### 6.2 Register Engine as Singleton
**File:** `KnockBox/Services/Registrations/Logic/LogicRegistrations.cs`
- Add `services.AddSingleton<ConsultTheCardGameEngine>()`

### 6.3 Wire Engine in LobbyService
**File:** `KnockBox/Services/Logic/Games/Shared/LobbyService.cs`
- Add case to the switch: `GameType.ConsultTheCard => serviceProvider.GetService<ConsultTheCardGameEngine>()`

---

## Acceptance Criteria
- [ ] `GameType.ConsultTheCard` exists with correct `Description` and `NavigationString` attributes
- [ ] `ConsultTheCardGameEngine` is registered as a singleton in DI
- [ ] `LobbyService` can resolve and return the `ConsultTheCardGameEngine` for the new game type
- [ ] `dotnet build` succeeds for the full solution
- [ ] Creating a ConsultTheCard lobby via the existing lobby flow works (manual verification)
