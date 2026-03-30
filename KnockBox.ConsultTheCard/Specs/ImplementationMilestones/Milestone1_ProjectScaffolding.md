# Milestone 1: Project Scaffolding

## Objective
Set up the project structure, references, and test project so that subsequent milestones can compile and test incrementally.

---

## Action Items

### 1.1 Clean Up Stub
- Delete `KnockBox.ConsultTheCard/Class1.cs`

### 1.2 Update `KnockBox.ConsultTheCard.csproj`
- Add `<ProjectReference>` to `KnockBox.Core`
- Add `<Using Include="Microsoft.Extensions.Logging" />`
- Add `<InternalsVisibleTo Include="KnockBox.ConsultTheCardTests" />`

### 1.3 Create Test Project
- Create `KnockBox.ConsultTheCardTests/KnockBox.ConsultTheCardTests.csproj`
  - Target: .NET 10.0
  - Framework: MSTest (`Microsoft.VisualStudio.TestTools.UnitTesting`)
  - Mocking: Moq
  - References: `KnockBox.ConsultTheCard`, `KnockBox.Core`
  - Add `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`
- Add the test project to the solution file (`KnockBox.slnx`)

### 1.4 Create Directory Structure
Create the following empty directories (with placeholder files or initial stubs as needed):
```
KnockBox.ConsultTheCard/
  Services/
    Logic/Games/ConsultTheCard/
      Data/
      FSM/
        States/
    State/Games/ConsultTheCard/
      Data/

KnockBox.ConsultTheCardTests/
  Unit/Logic/Games/ConsultTheCard/
```

---

## Acceptance Criteria
- [ ] `Class1.cs` is deleted
- [ ] `KnockBox.ConsultTheCard.csproj` references `KnockBox.Core` and has `InternalsVisibleTo`
- [ ] `KnockBox.ConsultTheCardTests` project exists with MSTest, Moq, and correct project references
- [ ] Test project is added to the solution
- [ ] `dotnet build` succeeds for the entire solution
- [ ] `dotnet test --filter "FullyQualifiedName~ConsultTheCard"` runs (even if 0 tests yet)
