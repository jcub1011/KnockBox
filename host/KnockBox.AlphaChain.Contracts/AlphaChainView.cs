namespace KnockBox.AlphaChain.Contracts;

/// <summary>A glanceable chip on a card — flattened from the server's <c>CardChip</c>.</summary>
/// <param name="Label">Short chip text (e.g. "+10", "×0.5", "FX").</param>
/// <param name="Color">CSS color value (typically a <c>var(--…)</c> token the theme defines).</param>
public readonly record struct ChipView(string Label, string Color);

/// <summary>
/// A modifier card flattened for the wire: the server resolves each card's live name,
/// description, accent and chips (which can depend on per-player room state) server-side
/// so the client renders pure data and never touches <c>IModifierCard</c>.
/// </summary>
/// <param name="Id">The card identity (also keys its icon glyph).</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Rules text (already resolved for this player's context).</param>
/// <param name="Accent">Standardized family accent, for border tinting.</param>
/// <param name="Chips">Glanceable chips in display order.</param>
public sealed record CardView(
    ModifierId Id,
    string Name,
    string Description,
    CardAccent Accent,
    IReadOnlyList<ChipView> Chips);

/// <summary>
/// One card's contribution as a word walks the Engine Bay (flattened <c>ScoreStep</c>).
/// <see cref="Description"/> (the card's rules text, for the hover tooltip on the game-over history
/// strip) is only populated in the game-over submission breakdowns — it is left empty on the
/// per-tick live replay to keep round projections lean.
/// </summary>
public sealed record ScoreStepView(
    ModifierId CardId,
    string Name,
    CardAccent Accent,
    bool Triggered,
    string ValueText,
    int RunningScore,
    string Description);

/// <summary>The full per-step trace of a scored word (flattened <c>ScoreBreakdown</c>).</summary>
public sealed record ScoreBreakdownView(
    string Word,
    int Seed,
    IReadOnlyList<ScoreStepView> Steps,
    int FinalBeforeTax,
    bool Taxed,
    int FinalScore);

/// <summary>
/// The most recent accepted word's scoring trace, projected for the center-stage replay
/// animation. The server pre-computes the derived <see cref="HasSteal"/>/<see cref="HasEffects"/>/
/// <see cref="HasAnimation"/>/<see cref="AnimationRows"/> flags the client used to read off the
/// server <c>ScoreReplay</c> record. Only projected when there is something to animate.
/// </summary>
public sealed record ScoreReplayView(
    int Sequence,
    Guid UserId,
    string DisplayName,
    ScoreBreakdownView Breakdown,
    int TaxBounty,
    IReadOnlyList<string> TaxCollectors,
    IReadOnlyList<EngineEffectEvent> Effects,
    bool HasSteal,
    bool HasEffects,
    bool HasAnimation,
    int AnimationRows);

/// <summary>
/// One accepted submission in the play feed / history (flattened <c>AlphaChainSubmission</c>).
/// <see cref="Engine"/> (the full per-card scoring trace) is only populated at game over — the
/// in-round recent-words strip needs only the word/score, so the heavy breakdown is omitted from
/// the ~4 Hz round projections and sent once for the post-game history screen.
/// </summary>
public sealed record SubmissionView(
    DateTimeOffset PlayedAt,
    Guid UserId,
    string DisplayName,
    string Word,
    int Score,
    bool ZeroPointTax,
    int TaxBounty,
    ScoreBreakdownView? Engine);

/// <summary>
/// An in-game player's projected state. The Engine Bay is flattened to <see cref="CardView"/>s
/// (no <c>IModifierCard</c> crosses the wire). Alpha Chain is fully symmetric — every player's
/// bay/score is public — so there is no per-recipient redaction here.
/// </summary>
public sealed record PlayerView(
    Guid UserId,
    string DisplayName,
    int Score,
    bool IsEliminated,
    int? EliminationOrder,
    bool HasLeft,
    int ModifierSlots,
    IReadOnlyList<CardView> EngineBay,
    IReadOnlyList<ModifierId> NewlyDealtModifierIds,
    int AccentSlot);

/// <summary>A single row of the final standings (flattened <c>PlayerResult</c>).</summary>
public sealed record PlayerResultView(
    Guid UserId,
    string DisplayName,
    int Score,
    bool Eliminated,
    int WordsPlayed);

/// <summary>Final standings (flattened <c>GameResults</c>; <c>Duration</c> as seconds).</summary>
public sealed record GameResultsView(
    IReadOnlyList<PlayerResultView> Rankings,
    Guid WinnerUserId,
    int TotalWordsPlayed,
    double DurationSeconds);

/// <summary>A lobby roster entry (the joined players shown before the match starts).</summary>
public sealed record RosterEntryView(Guid UserId, string DisplayName, bool IsHost);

/// <summary>
/// The per-recipient projection of an Alpha Chain game — everything the WASM UI renders,
/// with all modifier cards flattened to <see cref="CardView"/>s and phase deadlines surfaced
/// as absolute UTC timestamps the client counts down from. Alpha Chain holds no hidden state,
/// so the only per-recipient fields are the live card-derived flags (input mask, tunnel vision,
/// personal bans, era-ban exemption) the server resolves for <see cref="RecipientId"/>.
/// </summary>
public sealed record AlphaChainView(
    // ── Identity / lobby ────────────────────────────────────────────────
    Guid HostId,
    Guid RecipientId,
    bool IsJoinable,
    // ── Testing Bay (host-only dev card bench) ──────────────────────────
    // When true, this view is a projection of the throwaway bench scenario (god-mode
    // sandbox), not the real lobby/match; the client renders BenchView. CardCatalogue is
    // the full palette of dealable cards (empty unless IsBench).
    bool IsBench,
    IReadOnlyList<CardView> CardCatalogue,
    bool RecipientIsHost,
    bool RecipientIsParticipant,
    bool HostIsParticipant,
    int MinPlayerCount,
    int MaxPlayerCount,
    IReadOnlyList<RosterEntryView> Roster,
    AlphaChainSettings Settings,
    // ── Phase / progress ────────────────────────────────────────────────
    AlphaChainGamePhase Phase,
    int CurrentRound,
    int CurrentEra,
    Guid? CurrentPlayerId,
    IReadOnlyList<PlayerView> Players,
    // ── Chain state ─────────────────────────────────────────────────────
    string? LastWord,
    string? RequiredStartLetter,
    string? BannedLetter,
    bool AwaitingTransition,
    bool PendingTransitionIsGameOver,
    // ── Feed / replay / notices ─────────────────────────────────────────
    IReadOnlyList<SubmissionView> PlayFeed,
    ScoreReplayView? LatestReplay,
    IReadOnlyList<EngineEffectEvent> LatestEngineNotices,
    int EngineNoticeSequence,
    // ── Intermission ────────────────────────────────────────────────────
    IntermissionSubPhase IntermissionPhase,
    int OptimizationSubmittedCount,
    int OptimizationTotalCount,
    bool RecipientHasSubmittedOptimization,
    Guid? SniperBanUserId,
    bool RecipientIsSniperBanPicker,
    IReadOnlyList<string> LegalBanLetters,
    // ── Tutorials / results ─────────────────────────────────────────────
    TutorialKind CurrentTutorial,
    GameResultsView? Results,
    // ── Timing (absolute UTC + durations for CountdownClock) ─────────────
    DateTimeOffset? PhaseEndsAtUtc,
    int ShotClockDurationSeconds,
    DateTimeOffset? SubPhaseEndsAtUtc,
    int SubPhaseDurationSeconds,
    int CountdownDurationSeconds,
    double EngineAnimationSeconds,
    // ── Per-recipient live card flags ───────────────────────────────────
    bool RecipientHidesInput,
    bool RecipientMasksPreviousWord,
    string? RecipientPersonalBanLetter,
    IReadOnlyList<string> RecipientCardBanLetters,
    bool RecipientExemptFromEraBan);
