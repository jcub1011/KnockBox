# Milestone 7: Word Bank

## Objective
Create the word group CSV file and the `WordBank` class that loads and parses it at startup.

---

## Action Items

### 7.1 Create `WordPairs.csv`
**File:** `KnockBox.Codeword/Services/Logic/Games/Codeword/Data/WordPairs.csv`
- No header row
- Each row contains 2+ thematically related words, comma-separated
- Ship with 50-100 word groups initially
- Groups with more words are higher value (N words = N*(N-1) configurations)
- Follow design philosophy: "thematically adjacent but distinct" -- words share some associations but diverge on others

Example format:
```csv
Ocean,Lake
Guitar,Violin,Cello,Harp
Castle,Fortress,Palace
Sunrise,Sunset,Dawn,Dusk
Astronaut,Pilot
Mountain,Hill,Peak,Ridge,Summit
```

### 7.2 Configure `.csproj` for CSV
**File:** `KnockBox.Codeword/KnockBox.Codeword.csproj`
- Add: `<Content Include="Services\Logic\Games\Codeword\Data\WordPairs.csv" CopyToOutputDirectory="PreserveNewest" />`

### 7.3 Implement `WordBank.cs`
**File:** `KnockBox.Codeword/Services/Logic/Games/Codeword/Data/WordBank.cs`
- Static class
- Loads `WordPairs.csv` from disk: `File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Services/Logic/Games/Codeword/Data/WordPairs.csv"))`
- Returns `IReadOnlyList<WordGroup>`
- Validation:
  - Each row must have at least 2 words; rows with fewer are skipped with a warning log
  - Empty lines and whitespace-only lines are skipped
  - Whitespace is trimmed from words
- Does not throw on empty file (returns empty list)

---

## Acceptance Criteria
- [ ] `WordPairs.csv` exists with 50-100 word groups
- [ ] All word groups have at least 2 words per row
- [ ] Word groups are thematically adjacent but distinct (no too-similar or too-different pairs)
- [ ] `.csproj` configured to copy CSV to output directory
- [ ] `WordBank` loads and parses the CSV correctly
- [ ] Rows with < 2 words are skipped with a warning (not an exception)
- [ ] Empty/whitespace lines are skipped
- [ ] Whitespace is trimmed from all words
- [ ] Empty file returns empty list (no exception)
- [ ] `dotnet build` copies `WordPairs.csv` to output directory
