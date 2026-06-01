using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.AlphaChain.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Transient bootstrap state. Snapshots participants into <c>GamePlayers</c>,
    /// initializes the era/round counters, and immediately hands off to
    /// <see cref="RoundState"/> (the FSM chains the returned transition before any
    /// command is processed, so this state is never observed by the UI).
    /// </summary>
    public sealed class SetupState : IGameState<AlphaChainGameContext, AlphaChainCommand>
    {
        public ValueResult<IGameState<AlphaChainGameContext, AlphaChainCommand>?> OnEnter(AlphaChainGameContext context)
        {
            var state = context.State;

            // Snapshot every participant (host included when HostPlays is on — Participants,
            // not Players) into the per-player state dictionary.
            foreach (var entry in state.Participants)
            {
                state.GamePlayers[entry.User.Id] = new AlphaChainPlayerState
                {
                    UserId = entry.User.Id,
                    DisplayName = entry.DisplayName
                };
            }

            state.CurrentEra = 1;
            state.CurrentRound = 1;

            // Pick the match's banned letter from the configured letter class. Stored
            // lower-case so chain/contains checks against the normalized word are direct.
            state.BannedLetter = PickBannedLetter(context, state.Settings.BanMode);

            // First player has a free choice — no required start letter yet.
            state.RequiredStartLetter = null;

            context.Logger.LogDebug(
                "Alpha Chain FSM → SetupState ({count} participants, banned letter '{banned}')",
                state.GamePlayers.Count, state.BannedLetter);

            return new RoundState();
        }

        public Result OnExit(AlphaChainGameContext context) => Result.Success;

        public ValueResult<IGameState<AlphaChainGameContext, AlphaChainCommand>?> HandleCommand(
            AlphaChainGameContext context, AlphaChainCommand command) => null;

        // Lower-case letter pools the ban draws from, per BanLetterMode.
        private const string Vowels = "aeiou";
        private const string Consonants = "bcdfghjklmnpqrstvwxyz"; // 21 letters
        private const string AllLetters = "abcdefghijklmnopqrstuvwxyz";

        /// <summary>Draws a banned letter (lower-case) from the pool selected by <paramref name="mode"/>.</summary>
        private static char PickBannedLetter(AlphaChainGameContext context, BanLetterMode mode)
        {
            string pool = mode switch
            {
                BanLetterMode.Vowels => Vowels,
                BanLetterMode.Consonants => Consonants,
                _ => AllLetters,
            };
            return pool[context.Rng.GetRandomInt(pool.Length)];
        }
    }
}
