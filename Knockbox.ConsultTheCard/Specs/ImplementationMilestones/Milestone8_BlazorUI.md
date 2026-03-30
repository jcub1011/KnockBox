# Milestone 8: Blazor UI

## Objective
Build the full Blazor UI following the CardCounter pattern: one main lobby page (`DisposableComponent`) with phase-specific child components (`ComponentBase`).

---

## Action Items

### 8.1 Error Display Pattern
Use toast notifications (CardCounter pattern with `@key`-based re-render and `@onanimationend` auto-dismiss):
- **Clue rejections**: Ambiguous message ("Clue not accepted. Try a different word.") to avoid revealing secret words
- **Vote errors**: "You cannot vote for that player."
- **VoteToEndGame spam**: "You have already voted to end the game this round."
- **Informant guess timeout**: "Time's up -- guess forfeited."
- **General rejections**: "Action not available right now."

### 8.2 ConsultTheCardLobby (Main Page)
- Route: `@page "/room/consult-the-card/{ObfuscatedRoomCode}"`
- Extends: `DisposableComponent`
- Injects: `ConsultTheCardGameEngine`, `IGameSessionService`, `INavigationService`, `IUserService`, `ITickService`, `ILogger`
- `OnInitializedAsync`: Initialize user, get session, cast state, subscribe to `StateChangedEventManager`
- Host tick: Register via `TickService.RegisterTickCallback()` -- only host registers
- Phase switching: Render child component based on `GameState.GamePhase` and `GameState.IsJoinable`
- Timer display: If current FSM state implements `ITimedConsultTheCardGameState`, show countdown
- Kicked player detection: Check `GameState.KickedPlayers` in `OnAfterRender`
- Dispose: Unsubscribe state change listener, dispose tick registration

### 8.3 LobbyPhase
- Player list with host badge, kick buttons (host only)
- Host settings drawer: timer toggles, timeout durations, total games count
- Start button (host only, enabled when 4-8 players joined)
- Calls `GameEngine.StartAsync()`

### 8.4 SetupPhase
- Shows the player their secret word in a large card-like display
- Informant sees "???" card
- Brief phase (5s), auto-advances via timer
- No player actions needed (purely informational)

### 8.5 CluePhase
- Shows whose turn it is (highlighted player)
- Active player sees text input + submit button
- Non-active players see waiting indicator
- Clue history: submitted clues for this cycle (player name + clue word)
- Previously used clues shown as disabled hints
- Calls `GameEngine.SubmitClue()`

### 8.6 DiscussionPhase
- All submitted clues displayed prominently
- Player list with role-unknown indicators
- "Vote to End Game" button -- any alive player, once per cycle; shows progress (X of Y needed); disabled after voting
- Host "Advance to Vote" button
- Timer countdown
- Calls `GameEngine.VoteToEndGame()` or `GameEngine.AdvanceToVote()`

### 8.7 VotePhase
- Grid of alive players (excluding self) as vote targets
- Click target to cast vote
- Vote confirmation: selected player highlighted, confirm button
- Progress indicator: "X of Y votes cast"
- After voting: "Waiting for others..." with lock icon
- Calls `GameEngine.CastVote()`

### 8.8 RevealPhase
- If elimination: reveal eliminated player's name and role
- If tie: "No elimination -- tied vote" message
- If Informant eliminated: text input + "Guess the Agents' Word" button **only for eliminated Informant**; others see "The Informant is making their guess..." with countdown. One attempt. Correct = game over. Wrong/timeout = result shown, game continues.
- Round summary: vote tally breakdown
- Per-cycle score changes
- Auto-advances via timer (paused during Informant guess)

### 8.9 GameOverPhase
- Winner announcement with reason
- Scoreboard: all players ranked by score with roles revealed
- Scoring breakdown per player
- Multi-game progress: "Game X of Y"
- If more games: Host "Next Game" button -> `GameEngine.StartNextGame()`
- If final game: cumulative scoreboard, overall winner
- Host buttons: "Return to Lobby" / "Play Again"
- Calls `GameEngine.ReturnToLobby()` or `GameEngine.ResetGame()`

---

## Acceptance Criteria
- [ ] Main lobby page routes correctly and renders phase-specific components
- [ ] Host tick is registered only for the host player
- [ ] Phase switching renders the correct component for each `GamePhase`
- [ ] Timer countdown displays when FSM state is timed
- [ ] LobbyPhase shows player list, settings, and start button (host only, 4-8 players)
- [ ] SetupPhase shows secret word (or "???" for Informant) and auto-advances
- [ ] CluePhase shows turn indicator, input for active player, and clue history
- [ ] DiscussionPhase shows clues, end-game vote button with progress, and host advance
- [ ] VotePhase shows vote targets, confirmation, progress, and waiting state
- [ ] RevealPhase shows elimination result, Informant guess UI (when applicable), and vote tally
- [ ] GameOverPhase shows winner, scores, multi-game controls, and lobby return
- [ ] All error toasts use the correct ambiguous/generic messages
- [ ] Kicked players are detected and handled
- [ ] Component disposal cleans up subscriptions and tick registration
