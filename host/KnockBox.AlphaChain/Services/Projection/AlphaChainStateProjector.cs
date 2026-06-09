using KnockBox.AlphaChain.Contracts;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.Logic.Games.FSM.States;
using KnockBox.AlphaChain.Services.Logic.Scoring;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Projection;

namespace KnockBox.AlphaChain.Services.Projection
{
    /// <summary>
    /// Builds the per-recipient <see cref="AlphaChainView"/>. Alpha Chain holds <b>no hidden
    /// state</b> — every player's Engine Bay, score and the played-word feed are public — so the
    /// projection copies the full symmetric state and the only per-recipient fields are the live,
    /// card-derived flags the server resolves for <paramref name="recipientId"/> (input mask, tunnel
    /// vision, personal/card bans, era-ban exemption). Modifier cards are flattened to
    /// <see cref="CardView"/>s here so no <c>IModifierCard</c> ever crosses the wire (KB1007), and
    /// phase deadlines are surfaced as absolute UTC timestamps the client counts down from.
    /// <para>
    /// Runs inside <c>AbstractGameState.WithExclusiveRead</c> (the host's <c>GameViewCoordinator</c>
    /// holds the read lock), so it observes a consistent snapshot. It reproduces, server-side, the
    /// exact per-player selectors the old <c>AlphaChainGame.razor.cs</c> computed for the local user.
    /// </para>
    /// </summary>
    public sealed class AlphaChainStateProjector(IModifierCardFactory? cardFactory = null)
        : AbstractStateProjector<AlphaChainGameState, AlphaChainView>
    {
        // Static per-card rules text, built once, for the game-over history strip's hover tooltip.
        // Resolved with an empty context — card descriptions are static (mirrors the old
        // SubmissionHistoryPanel, which built the same map from IModifierCardFactory server-side).
        // Empty when no factory is supplied (e.g. a bare projector in a unit test).
        private readonly IReadOnlyDictionary<ModifierId, string> _cardDescriptions = BuildCardDescriptions(cardFactory);

        private static IReadOnlyDictionary<ModifierId, string> BuildCardDescriptions(IModifierCardFactory? factory)
        {
            if (factory is null) return new Dictionary<ModifierId, string>();
            var ctx = new EngineEvaluationContext(string.Empty, [], []);
            return ModifierCardFactory.AllDealableIds.ToDictionary(
                id => id, id => factory.CreateCard(ctx, id).GetDescription(ctx));
        }

        // The full palette of dealable cards (flattened to CardView), for the Testing Bay. Built once
        // on first use and only when the bench is active, so normal projections never pay for it.
        private IReadOnlyList<CardView>? _cardCatalogue;

        private IReadOnlyList<CardView> CardCatalogue()
        {
            if (_cardCatalogue is not null) return _cardCatalogue;
            if (cardFactory is null) return _cardCatalogue = [];
            var ctx = new EngineEvaluationContext(string.Empty, [], []);
            return _cardCatalogue = ModifierCardFactory.AllDealableIds
                .Select(id => ToCardView(cardFactory.CreateCard(ctx, id), ctx))
                .ToList();
        }

        public override AlphaChainView ProjectFor(AlphaChainGameState state, Guid recipientId)
        {
            // Testing Bay: project the throwaway bench scenario's INNER state (god-mode, all-public)
            // instead of the lobby, keeping the real lobby host id so the host-only bench UI gates.
            if (state.Bench?.State is { } benchState)
                return ProjectCore(benchState, recipientId, hostId: state.Host.Id, isBench: true);
            return ProjectCore(state, recipientId, hostId: state.Host.Id, isBench: false);
        }

        private AlphaChainView ProjectCore(AlphaChainGameState state, Guid recipientId, Guid hostId, bool isBench)
        {
            var roster = state.Players
                .Select(e => new RosterEntryView(e.User.Id, e.DisplayName, e.User.Id == state.Host.Id))
                .ToList();

            // In-game players, in turn order when one exists (so the lineup is stable across re-projections).
            var turnOrder = state.TurnManager.TurnOrder;
            IEnumerable<AlphaChainPlayerState> ordered = turnOrder.Count > 0
                ? turnOrder.Where(state.GamePlayers.ContainsKey).Select(id => state.GamePlayers[id])
                : state.GamePlayers.Values;
            var players = ordered.Select(p => ToPlayerView(state, p)).ToList();

            bool gameOver = state.Phase == AlphaChainGamePhase.GameOver;

            // Play feed, newest-first (matches the old page's PlayFeed). The per-card engine trace
            // is only attached at game over — the in-round recent strip needs only word/score.
            var playFeed = state.SubmissionHistory
                .AsEnumerable()
                .Reverse()
                .Select(s => ToSubmissionView(s, includeEngine: gameOver, _cardDescriptions))
                .ToList();

            // The live game only animates plays with something to show; the bench always wants the
            // last word's breakdown (incl. taxed/zero-score plays), matching the old card-bench page.
            var replay = state.LatestScoreReplay is { } r && (isBench || r.HasAnimation)
                ? ToReplayView(r)
                : null;

            // ── Per-recipient live card flags (reproduce AlphaChainGame.razor.cs) ──
            var me = state.GamePlayers.GetValueOrDefault(recipientId);
            bool recipientIsParticipant = me is not null;
            bool hidesInput = me?.EngineBay.Any(c => c is IInputMask) == true;
            bool masksPrev = me?.EngineBay.Any(c => c is IPreviousWordMask) == true;

            string? personalBan = null;
            IReadOnlyList<string> cardBans = [];
            bool exemptFromEraBan = false;
            if (me is not null)
            {
                var meCtx = BadgeContextFor(state, me);
                if (meCtx.Service<IHijackBanService>()?.Peek(me) is { } hijack)
                    personalBan = char.ToUpperInvariant(hijack).ToString();

                if (meCtx.Service<ICardBanService>() is { } bans)
                    cardBans = bans.BansFor(me)
                        .Select(c => char.ToUpperInvariant(c).ToString())
                        .Distinct()
                        .OrderBy(s => s, StringComparer.Ordinal)
                        .ToList();

                exemptFromEraBan = state.BannedLetter is not null
                    && EngineEffectResolver.LastPlaceUserId(state) == recipientId;
            }

            ComputeTiming(state, out var subPhaseDurationSeconds);

            return new AlphaChainView(
                // identity / lobby
                HostId: hostId,
                RecipientId: recipientId,
                IsJoinable: state.IsJoinable,
                IsBench: isBench,
                CardCatalogue: isBench ? CardCatalogue() : [],
                RecipientIsHost: recipientId == hostId,
                RecipientIsParticipant: recipientIsParticipant,
                HostIsParticipant: state.HostIsParticipant,
                MinPlayerCount: 2,
                MaxPlayerCount: 8,
                Roster: roster,
                Settings: state.Settings,
                // phase / progress
                Phase: state.Phase,
                CurrentRound: state.CurrentRound,
                CurrentEra: state.CurrentEra,
                CurrentPlayerId: state.TurnManager.CurrentPlayer,
                Players: players,
                // chain state
                LastWord: state.LastWord,
                RequiredStartLetter: state.RequiredStartLetter is { } rs ? char.ToUpperInvariant(rs).ToString() : null,
                BannedLetter: state.BannedLetter is { } bl ? char.ToUpperInvariant(bl).ToString() : null,
                AwaitingTransition: state.PendingTransitionAt is not null,
                PendingTransitionIsGameOver: state.PendingTransitionIsGameOver,
                // feed / replay / notices
                PlayFeed: playFeed,
                LatestReplay: replay,
                LatestEngineNotices: state.LatestEngineNotices.ToList(),
                EngineNoticeSequence: state.EngineNoticeSequence,
                // intermission
                IntermissionPhase: state.IntermissionPhase,
                OptimizationSubmittedCount: state.OptimizationSubmissions.Values.Count(s => s.Submitted),
                OptimizationTotalCount: state.OptimizationSubmissions.Count,
                RecipientHasSubmittedOptimization:
                    state.OptimizationSubmissions.TryGetValue(recipientId, out var sub) && sub.Submitted,
                SniperBanUserId: state.SniperBanUserId,
                RecipientIsSniperBanPicker: state.SniperBanUserId == recipientId,
                LegalBanLetters: BanLetterPool.For(state.Settings.BanMode)
                    .Select(c => char.ToUpperInvariant(c).ToString())
                    .ToList(),
                // tutorials / results
                CurrentTutorial: state.CurrentTutorial,
                Results: ToResultsView(state.Results),
                // timing
                PhaseEndsAtUtc: state.Phase == AlphaChainGamePhase.Round ? state.PhaseEndTime : null,
                ShotClockDurationSeconds: state.Settings.ShotClockSeconds,
                SubPhaseEndsAtUtc: state.SubPhaseEndTime,
                SubPhaseDurationSeconds: subPhaseDurationSeconds,
                CountdownDurationSeconds: state.Settings.PreRoundCountdownSeconds,
                EngineAnimationSeconds: state.Settings.EngineAnimationSeconds,
                // per-recipient live card flags
                RecipientHidesInput: hidesInput,
                RecipientMasksPreviousWord: masksPrev,
                RecipientPersonalBanLetter: personalBan,
                RecipientCardBanLetters: cardBans,
                RecipientExemptFromEraBan: exemptFromEraBan);
        }

        private static PlayerView ToPlayerView(AlphaChainGameState state, AlphaChainPlayerState p)
        {
            var ctx = BadgeContextFor(state, p);
            var bay = p.EngineBay.Select(c => ToCardView(c, ctx)).ToList();
            return new PlayerView(
                UserId: p.UserId,
                DisplayName: p.DisplayName,
                Score: p.Score,
                IsEliminated: p.IsEliminated,
                EliminationOrder: p.EliminationOrder,
                HasLeft: p.HasLeft,
                ModifierSlots: p.ModifierSlots,
                EngineBay: bay,
                NewlyDealtModifierIds: p.NewlyDealtModifierIds.ToList(),
                AccentSlot: AccentSlot(state, p.UserId));
        }

        /// <summary>
        /// A display-only evaluation context for <paramref name="player"/>'s Engine Bay carrying the
        /// room-state services, so each card flattens with its live status chip (e.g. the Titanium
        /// Mirror's decayed "×0.7"). Mirrors the old page's <c>BadgeContextFor</c>.
        /// </summary>
        private static EngineEvaluationContext BadgeContextFor(AlphaChainGameState state, AlphaChainPlayerState player)
            => new EngineEvaluationContext(string.Empty, Array.Empty<char>(), new[] { player })
            {
                Services = state.Context?.EvaluationServices,
                PlayerIndex = 0,
            }.WithBay(player.EngineBay);

        private static CardView ToCardView(IModifierCard card, EngineEvaluationContext ctx)
            => new(
                Id: card.GetId(),
                Name: card.GetName(),
                Description: card.GetDescription(ctx),
                Accent: card.GetAccent(),
                Chips: card.GetChips(ctx).Select(ch => new ChipView(ch.Label, ch.Color)).ToList());

        private static SubmissionView ToSubmissionView(
            AlphaChainSubmission s, bool includeEngine, IReadOnlyDictionary<ModifierId, string> descriptions)
            => new(
                PlayedAt: s.PlayedAt,
                UserId: s.UserId,
                DisplayName: s.DisplayName,
                Word: s.Word,
                Score: s.Score,
                ZeroPointTax: s.ZeroPointTax,
                TaxBounty: s.TaxBounty,
                // Only the game-over history needs the per-card rules text (for its hover tooltip).
                Engine: includeEngine ? ToBreakdownView(s.Engine, descriptions) : null);

        private static ScoreReplayView ToReplayView(ScoreReplay r)
            => new(
                Sequence: r.Sequence,
                UserId: r.UserId,
                DisplayName: r.DisplayName,
                // Live replay carries no per-step descriptions (kept lean for the ~4 Hz round stream).
                Breakdown: ToBreakdownView(r.Breakdown, descriptions: null),
                TaxBounty: r.TaxBounty,
                TaxCollectors: r.TaxCollectors?.ToList() ?? [],
                Effects: r.Effects?.ToList() ?? [],
                HasSteal: r.HasSteal,
                HasEffects: r.HasEffects,
                HasAnimation: r.HasAnimation,
                AnimationRows: r.AnimationRows);

        private static ScoreBreakdownView ToBreakdownView(
            ScoreBreakdown b, IReadOnlyDictionary<ModifierId, string>? descriptions)
            => new(
                Word: b.Word,
                Seed: b.Seed,
                Steps: b.Steps.Select(s => ToStepView(s, descriptions)).ToList(),
                FinalBeforeTax: b.FinalBeforeTax,
                Taxed: b.Taxed,
                FinalScore: b.FinalScore);

        private static ScoreStepView ToStepView(ScoreStep s, IReadOnlyDictionary<ModifierId, string>? descriptions)
            => new(s.CardId, s.Name, s.Accent, s.Triggered, s.ValueText, s.RunningScore,
                Description: descriptions is not null && descriptions.TryGetValue(s.CardId, out var d) ? d : string.Empty);

        private static GameResultsView? ToResultsView(GameResults? results)
            => results is null
                ? null
                : new GameResultsView(
                    Rankings: results.Rankings
                        .Select(r => new PlayerResultView(r.UserId, r.DisplayName, r.Score, r.Eliminated, r.WordsPlayed))
                        .ToList(),
                    WinnerUserId: results.WinnerUserId,
                    TotalWordsPlayed: results.TotalWordsPlayed,
                    DurationSeconds: results.Duration.TotalSeconds);

        /// <summary>Maps a player to a turn-order accent slot (1-based, wraps at 6). Mirrors the page.</summary>
        private static int AccentSlot(AlphaChainGameState state, Guid userId)
        {
            int i = 0;
            foreach (var id in state.TurnManager.TurnOrder)
            {
                if (id == userId) return (i % 6) + 1;
                i++;
            }
            return (Math.Abs(userId.GetHashCode()) % 6) + 1;
        }

        /// <summary>
        /// The current sub-phase countdown duration (seconds), keyed off phase + intermission sub-phase
        /// exactly as the old page's <c>SubPhaseDuration</c>. The pre-round Countdown phase uses
        /// <c>PreRoundCountdownSeconds</c> (projected separately as <c>CountdownDurationSeconds</c>).
        /// </summary>
        private static void ComputeTiming(AlphaChainGameState state, out int subPhaseDurationSeconds)
        {
            subPhaseDurationSeconds = state.Phase == AlphaChainGamePhase.Intermission
                ? state.IntermissionPhase switch
                {
                    IntermissionSubPhase.Optimization => state.Settings.IntermissionCardSelectSeconds,
                    IntermissionSubPhase.TaxTutorial => (int)Math.Round(TutorialState.DurationFor(TutorialKind.Tax).TotalSeconds),
                    IntermissionSubPhase.SniperBan => state.Settings.SniperBanSeconds,
                    _ => 0
                }
                : 0;
        }
    }
}
