using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Components;
using KnockBox.Core.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.LinkedList.Services.State.Games
{
    public class LinkedListGameState(
        User host,
        ILogger<LinkedListGameState> logger)
        : AbstractGameState(host, logger)
    {
        /// <summary>The current phase of the game.</summary>
        public LinkedListGamePhase Phase { get; private set; } = LinkedListGamePhase.Setup;

        /// <summary>
        /// Updates the current phase. Notification is intentionally NOT raised here —
        /// callers run inside <c>Execute</c>/<c>ExecuteAsync</c>, which fires
        /// <c>NotifyStateChanged</c> exactly once after the lock is released.
        /// </summary>
        public void SetPhase(LinkedListGamePhase phase) => Phase = phase;

        /// <summary>Drives the submitting-player rotation.</summary>
        public TurnManager TurnManager { get; } = new();

        /// <summary>All player states, keyed by player id.</summary>
        public ConcurrentDictionary<string, LinkedListPlayerState> GamePlayers { get; } = new();

        // ── Round data (single shared chain for Collective; Groups extends this in M5) ──

        public string StartWord { get; set; } = "";
        public string DestinationWord { get; set; } = "";
        public string CarriedWord { get; set; } = "";
        public readonly List<ChainLink> Chain = [];
        public readonly List<RejectionInfo> RejectionLog = [];
        public int RejectionsThisTurn { get; set; }
        public bool DestinationReached { get; set; }

        // ── Auditor (rotation logic lands in M4; M1 just assigns the first one) ──

        public string AuditorPlayerId { get; set; } = "";

        /// <summary>
        /// Host-configurable match rules. Always replaced atomically via
        /// <see cref="UpdateSettings"/>; the setter is private so callers can't
        /// bypass the lock.
        /// </summary>
        public LinkedListSettings Settings { get; private set; } = new();

        /// <summary>
        /// Atomically replaces <see cref="Settings"/> with <paramref name="mutate"/>'s
        /// result and reflects the new <c>HostPlaysGame</c> value into
        /// <see cref="AbstractGameState.HostIsParticipant"/> in the same critical
        /// section, so subscribers observe a single consistent transition.
        /// </summary>
        public Result UpdateSettings(Func<LinkedListSettings, LinkedListSettings> mutate) =>
            Execute(() =>
            {
                Settings = mutate(Settings);
                SetHostIsParticipant(Settings.HostPlaysGame);
            });
    }

    #region Enums

    public enum LinkedListGamePhase { Setup, Playing, RoundOver, GameOver }

    #endregion

    #region Records

    /// <summary>An accepted link in the chain (<c>FromWord</c> → <c>ToWord</c>).</summary>
    public sealed record ChainLink(string FromWord, string ToWord, string PlayerId, string PlayerName, bool IsLoop);

    /// <summary>A rejected attempt and the Auditor's reason.</summary>
    public sealed record RejectionInfo(string PlayerId, string AttemptedWord, string Reason);

    /// <summary>A player's proposed next word (the first word is the carried word).</summary>
    public sealed record Submission(string PlayerId, string ProposedWord);

    public sealed class LinkedListPlayerState
    {
        public required string PlayerId { get; init; }
        public required string DisplayName { get; init; }
        public int AcceptedPairs { get; set; }     // for "fewest guesses" + superlatives
        public int RejectionsReceived { get; set; }
        // group id / time accrual added in later milestones
    }

    #endregion
}
