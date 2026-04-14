# Phase 7: UI (Razor Pages and Components)

## Goal

Build the Blazor Server UI for all game phases. After this phase, Hidden Agenda is fully playable in the browser with real-time updates, board visualization, card displays, and scoreboard.

## Prerequisites

All previous phases (1-6) must be complete. All game logic exists -- this phase is pure presentation.

---

## Platform Context

### Lobby page pattern (from `KnockBox.Codeword/Pages/CodewordLobby.razor.cs`)

The lobby page is the single Razor page for the game (matching the `@page` route). It:
1. Validates the session and redirects home if invalid
2. Casts state to the game-specific type
3. Subscribes to `StateChangedEventManager` for real-time re-renders
4. Registers a **tick callback** via `ITickService` (host only, at TicksPerSecond rate)
5. Contains a phase-switching render block that delegates to sub-components per phase
6. Disposes subscriptions in `Dispose()`

```csharp
// Key injections
[Inject] protected HiddenAgendaGameEngine GameEngine { get; set; } = default!;
[Inject] protected IGameSessionService GameSessionService { get; set; } = default!;
[Inject] protected INavigationService NavigationService { get; set; } = default!;
[Inject] protected IUserService UserService { get; set; } = default!;
[Inject] protected ITickService TickService { get; set; } = default!;
[Inject] protected ILogger<HiddenAgendaLobby> Logger { get; set; } = default!;

// Tick registration (host only, in OnInitializedAsync)
if (IsHost())
{
    var tickResult = TickService.RegisterTickCallback(() =>
    {
        if (GameState?.Context is not null)
            GameEngine.Tick(GameState.Context, DateTimeOffset.UtcNow);
    }, tickInterval: TickService.TicksPerSecond);
    if (tickResult.TryGetSuccess(out var sub))
        _tickSubscription = sub;
}
```

### Razor page pattern

```razor
@page "/room/hidden-agenda/{ObfuscatedRoomCode}"
@inherits DisposableComponent

@switch (GameState.Phase)
{
    case GamePhase.Lobby:
        <LobbyPhase GameState="GameState" Engine="GameEngine" UserService="UserService" />
        break;
    case GamePhase.RoundSetup:
        <RoundSetupPhase GameState="GameState" UserService="UserService" />
        break;
    // ... etc
}
```

### DisposableComponent base class

From `KnockBox.Core.Components.Shared`. Provides:
- `ComponentDetached` CancellationToken
- `Dispose()` virtual method (always call `base.Dispose()`)

### Static assets

Plugin static assets served at `/_content/KnockBox.HiddenAgenda/`. CSS bundle: `_content/KnockBox.HiddenAgenda/KnockBox.HiddenAgenda.styles.css`.

---

## Files to Modify

### `Pages/HiddenAgendaLobby.razor`

Replace current simple lobby with the full phase-switching entry point.

```razor
@page "/room/hidden-agenda/{ObfuscatedRoomCode}"
@inherits DisposableComponent

<PageTitle>Hidden Agenda</PageTitle>
<HeadContent>
    <link href="_content/KnockBox.HiddenAgenda/KnockBox.HiddenAgenda.styles.css" rel="stylesheet" />
</HeadContent>

@if (GameState == null)
{
    <div>Loading...</div>
}
else
{
    @switch (GameState.Phase)
    {
        case GamePhase.Lobby:
            <LobbyPhase GameState="GameState" Engine="GameEngine"
                        UserService="UserService" Config="GameState.Config" />
            break;
        case GamePhase.RoundSetup:
            <RoundSetupPhase GameState="GameState" UserService="UserService" />
            break;
        case GamePhase.EventCardPhase:
        case GamePhase.SpinPhase:
        case GamePhase.MovePhase:
        case GamePhase.DrawPhase:
            <TurnPhase GameState="GameState" Engine="GameEngine"
                       UserService="UserService" />
            break;
        case GamePhase.GuessPhase:
            <GuessPhase GameState="GameState" Engine="GameEngine"
                        UserService="UserService" />
            break;
        case GamePhase.FinalGuess:
            <FinalGuessPhase GameState="GameState" Engine="GameEngine"
                             UserService="UserService" />
            break;
        case GamePhase.Reveal:
            <RevealPhase GameState="GameState" UserService="UserService" />
            break;
        case GamePhase.RoundOver:
            <RoundOverPhase GameState="GameState" Engine="GameEngine"
                            UserService="UserService" />
            break;
        case GamePhase.MatchOver:
            <MatchOverPhase GameState="GameState" Engine="GameEngine"
                            UserService="UserService" />
            break;
    }
}
```

### `Pages/HiddenAgendaLobby.razor.cs`

Update to:
1. Add `ITickService` injection
2. Register tick callback for host (same pattern as Codeword)
3. Add tick subscription disposal
4. Keep existing session validation logic

---

## Files to Create (Pages)

### `Pages/LobbyPhase.razor` / `.razor.cs`

Extracted from current HiddenAgendaLobby. Shows:
- Player list with kick buttons (host only)
- Config controls: round count slider (3-5), timer toggle, pool rotation dropdown
- Min/max player count display (3-6)
- Room code display
- Start Game button (host only, enabled when player count >= 3)

### `Pages/RoundSetupPhase.razor` / `.razor.cs`

Brief display at round start. Shows:
- Round number ("Round 2 of 4")
- The full dossier (all tasks in the pool, organized by category) -- public information
- Player's 3 secret tasks (highlighted/private -- only visible to this player)
- Collection targets as a reference
- Auto-advances (timed state, no player action needed)

### `Pages/TurnPhase.razor` / `.razor.cs`

**Single unified turn page** that renders different sub-sections based on the current phase sub-step. This keeps the board and collection tracks persistent across the turn, preventing flicker.

**Always visible (sidebar/header):**
- Board view (BoardView component)
- Collection progress tracks (CollectionTracks component)
- Current player indicator ("Player X's turn")
- Turn counter ("Turn 5 of 10")
- Guess countdown status (if active: "2 turns remaining")
- Play history log (scrollable list of past card plays)

**Phase-specific content area:**
- `GamePhase.EventCardPhase`: Show held event card, Play/Skip buttons, target player selection (for Catalog/Detour). For Detour: show a player-selection dropdown (excluding self) to choose the target whose last movement to copy; validate target has at least one completed move (`LastMoveDestination != null`). For Catalog: show a player-selection dropdown (excluding self) to choose the target to investigate. Only current player sees action buttons.
- `GamePhase.SpinPhase`: Spinner animation (SpinnerWheel component), Spin button for current player. Show result after spin.
- `GamePhase.MovePhase`: Board highlights reachable spaces. Click on a highlighted space to move. Show available destinations list as fallback.
- `GamePhase.DrawPhase`: At Curation Spot: display 3 drawn cards (CurationCardPicker component). At Event Spot with choice: show current + new card, keep/swap buttons. Show card effects clearly.

**For non-current players:** Show the board/collection state, current player's actions as they happen (via state subscription), and the player's own secret tasks in a collapsible panel (DossierPanel).

**Private information filtering:** The UI must filter private information based on the current user. Use `UserService.CurrentUser.Id == player.PlayerId` checks:
- `HeldEventCard` -- only show the card type to the holding player; other players see only that a card is held (boolean: `player.HeldEventCard != null`).
- `SecretTasks` -- only rendered for the owning player.
- `CatalogRevealedCards` -- only rendered for the player who used Catalog.
- `GuessSubmission` -- hidden until the Reveal phase.

### `Pages/GuessPhase.razor` / `.razor.cs`

Guess submission UI for the current player:
- For each opponent: 3 dropdowns/selectors to assign tasks from the dossier
- DossierPanel for reference
- Submit Guess / Skip buttons
- Countdown status display
- Other players see "Waiting for [Player]'s guess..."

### `Pages/FinalGuessPhase.razor` / `.razor.cs`

Same as GuessPhase but for the final guess window:
- All non-guessing players can submit simultaneously
- Timer countdown display
- Players who already guessed see "Waiting for other players..."

### `Pages/RevealPhase.razor` / `.razor.cs`

Results display:
- Each player's 3 secret tasks revealed with completion status (checkmark/X)
- Guess accuracy breakdown per player (who guessed what correctly)
- Points earned this round (task points + guess points breakdown)
- Timed auto-advance

### `Pages/RoundOverPhase.razor` / `.razor.cs`

Round summary:
- Cumulative scoreboard (sorted by total score)
- Round-by-round breakdown table
- "Next Round" button (host only)
- If final round: button says "View Final Results"

### `Pages/MatchOverPhase.razor` / `.razor.cs`

Final results:
- Winner announcement with total score
- Full score history (all rounds)
- Final standings
- "Play Again" / "Return to Lobby" buttons (host only)

---

## Files to Create (Components)

### `Components/BoardView.razor` / `.razor.css`

Visual representation of The Grand Circuit board.

**Approach:** SVG or CSS grid layout showing:
- 4 wings as labeled regions
- Spaces as circles/nodes connected by lines
- Player tokens (colored dots) at their positions
- During MovePhase: highlighted reachable spaces (clickable for current player)
- Spot type indicators (curation vs event)
- Shortcut corridors visually distinct

**Props:** `HiddenAgendaGameState GameState`, `Action<int>? OnSpaceClicked` (for move selection)

### `Components/CollectionTracks.razor` / `.razor.css`

Progress bars for all 5 collections.
- Name, current value / target value
- Visual fill bar (width proportional to progress/target)
- Completion indicator when target reached
- Color-coded by wing

**Props:** `Dictionary<CollectionType, int> Progress`

### `Components/CurationCardPicker.razor` / `.razor.css`

Card selection during DrawPhase.
- Display 3 cards as styled card elements
- Each card shows: type badge (Acquire/Remove/Trade), description, effects list
- For Trade cards: show both options (A and B) with selection
- Clickable selection with confirm button
- Only active for current player

**Props:** `IReadOnlyList<CurationCard> Cards`, `Action<int> OnCardSelected`

### `Components/DossierPanel.razor` / `.razor.css`

Collapsible/expandable panel showing the full task pool.
- Organized by category (Devotion, Style, Movement, Neglect, Rivalry)
- Each task: ID, description, difficulty, points
- Searchable/filterable (optional)
- Highlight player's own tasks (private)
- Used during gameplay for reference and during guess submission for selection

**Props:** `IReadOnlyList<SecretTask> TaskPool`, `List<SecretTask>? PlayerTasks` (for highlighting)

### `Components/GuessForm.razor` / `.razor.css`

Reusable guess entry form used by both GuessPhase and FinalGuessPhase.
- For each opponent: display name + 3 task selection slots
- Task selection: dropdown or drag-from-dossier
- Validation feedback (must select 3 unique tasks per opponent)
- Submit / Clear buttons

**Props:** `IReadOnlyList<SecretTask> TaskPool`, `IReadOnlyList<HiddenAgendaPlayerState> Opponents`, `Action<Dictionary<string, List<string>>> OnSubmit`

### `Components/SpinnerWheel.razor` / `.razor.css`

Animated spinner showing range 3-12.
- Visual spinner element (CSS animation or SVG rotation)
- Spin button triggers animation
- Displays result after animation
- Non-interactive for non-current players (shows result after current player spins)

**Props:** `int? Result`, `Action? OnSpin`, `bool IsCurrentPlayer`

### `Components/HiddenAgendaTile.razor` (update existing)

Update the home page tile to reflect the game's theme. Show "Hidden Agenda" name with an art gallery motif.

---

## Error Handling Pattern

Add an error toast mechanism to the lobby page (matching Codeword pattern):

```csharp
// In code-behind
private string? _errorMessage;
private int _errorKey;

protected void ShowError(string message)
{
    _errorMessage = message;
    _errorKey++;
}

// Wrap engine calls:
protected async Task DoAction(Func<Result> action)
{
    var result = action();
    if (result.IsFailure && result.TryGetFailure(out var err))
        ShowError(err.PublicMessage);
}
```

```razor
@if (_errorMessage != null)
{
    <div class="error-toast" @key="_errorKey">@_errorMessage</div>
}
```

---

## CSS Approach

Use scoped CSS (`.razor.css` files) for component-specific styling. The plugin's scoped CSS bundle is auto-generated and served at `/_content/KnockBox.HiddenAgenda/KnockBox.HiddenAgenda.styles.css`.

Design theme: Art gallery atmosphere -- rich colors (deep burgundy, gold, cream), elegant typography, card-like UI elements, gallery frame aesthetics.

---

## Verification

1. `dotnet build` compiles
2. Start the app: `dotnet run --project KnockBox/KnockBox.csproj`
3. Navigate to home page, find "Hidden Agenda" tile
4. Create a lobby, have 3+ players join (use multiple browser tabs/windows)
5. Test full flow:
   - Lobby: player list, config, start game
   - Round Setup: dossier visible, secret tasks shown to each player
   - Turn cycle: event card -> spin -> move -> draw -> guess
   - Board updates with player positions
   - Collections update when cards played
   - Guess submission works, countdown triggers
   - Round end -> final guess -> reveal -> scoring
   - Multi-round: next round resets properly
   - Match end: winner shown, play again works
6. Test edge cases:
   - Player disconnect mid-game
   - Timer timeout (let timers expire, verify auto-actions)
   - Min players (3) and max players (6)
   - All rounds complete
7. Verify real-time: actions by one player immediately visible to others
