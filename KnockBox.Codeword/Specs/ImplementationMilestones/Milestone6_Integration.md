# Milestone 6: Integration

## Objective
Wire the new game engine into the existing KnockBox application so that Codeword lobbies can be created and joined.

---

## Action Items

### 6.1 Register Game Type
**File:** `KnockBox/Services/Navigation/Games/GameTypes.cs`
- Add `Codeword` enum variant
- Add `[Description("Codeword")]` attribute
- Add `[NavigationString("codeword")]` attribute

### 6.2 Register Engine as Singleton
**File:** `KnockBox/Services/Registrations/Logic/LogicRegistrations.cs`
- Add `services.AddSingleton<CodewordGameEngine>()`

### 6.3 Wire Engine in LobbyService
**File:** `KnockBox/Services/Logic/Games/Shared/LobbyService.cs`
- Add case to the switch: `GameType.Codeword => serviceProvider.GetService<CodewordGameEngine>()`

---

## Acceptance Criteria
- [ ] `GameType.Codeword` exists with correct `Description` and `NavigationString` attributes
- [ ] `CodewordGameEngine` is registered as a singleton in DI
- [ ] `LobbyService` can resolve and return the `CodewordGameEngine` for the new game type
- [ ] `dotnet build` succeeds for the full solution
- [ ] Creating a Codeword lobby via the existing lobby flow works (manual verification)
