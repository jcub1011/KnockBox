using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Services.State.Games;

namespace KnockBox.LinkedList.Services.Logic.Games
{
    public class LinkedListGameEngine(
        WordPairSource wordPairSource,
        IRandomNumberService randomNumberService,
        ILogger<LinkedListGameEngine> logger,
        ILogger<LinkedListGameState> stateLogger)
        : AbstractGameEngine(minPlayerCount: 3, maxPlayerCount: 10)
    {
        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return Task.FromResult(ValueResult<AbstractGameState>.FromError("Failed to create game state.", $"Parameter {nameof(host)} was null."));

            var gameState = new LinkedListGameState(host, stateLogger);
            gameState.Execute(() => gameState.SetJoinable(true));
            logger.LogInformation("Created gameState with user [{userId}] as host.", host.Id);
            return Task.FromResult<ValueResult<AbstractGameState>>(gameState);
        }

        protected override Task<Result> StartAsyncCore(AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not LinkedListGameState gameState)
                return Task.FromResult(Result.FromError("Error starting game.", $"Game state of type [{(state?.GetType().Name ?? "null")}] couldn't be cast to type [{nameof(LinkedListGameState)}]."));

            var executeResult = gameState.Execute(() =>
            {
                // Build the participant roster (respects HostIsParticipant).
                var participants = gameState.Participants;
                var participantIds = participants.Select(p => p.User.Id).ToList();

                gameState.GamePlayers.Clear();
                foreach (var entry in participants)
                {
                    gameState.GamePlayers[entry.User.Id] = new LinkedListPlayerState
                    {
                        PlayerId = entry.User.Id,
                        DisplayName = entry.DisplayName,
                    };
                }

                gameState.TurnManager.SetTurnOrder(participantIds);

                // Words: honor host-chosen values from the lobby, else pick a curated pair.
                if (string.IsNullOrWhiteSpace(gameState.StartWord) || string.IsNullOrWhiteSpace(gameState.DestinationWord))
                {
                    var pair = wordPairSource.Random(randomNumberService);
                    gameState.StartWord = pair.Start;
                    gameState.DestinationWord = pair.Destination;
                }

                gameState.CarriedWord = gameState.StartWord;
                gameState.DestinationReached = false;
                gameState.Chain.Clear();
                gameState.RejectionLog.Clear();
                gameState.RejectionsThisTurn = 0;

                // Assign the first Auditor: the host-chosen id if valid, else the first
                // participant who is not the current submitter. M2 enforces the
                // active-player ≠ Auditor rule; M1 only records the choice.
                var currentSubmitter = gameState.TurnManager.CurrentPlayer;
                bool hostChoiceValid = !string.IsNullOrEmpty(gameState.AuditorPlayerId)
                    && participantIds.Contains(gameState.AuditorPlayerId);
                if (!hostChoiceValid)
                {
                    gameState.AuditorPlayerId =
                        participantIds.FirstOrDefault(id => id != currentSubmitter) ?? "";
                }

                gameState.SetJoinable(false);
                gameState.SetPhase(LinkedListGamePhase.Playing);
            });

            if (executeResult.TryGetFailure(out var error))
            {
                logger.LogError("Failed to start Linked List game: {Error}", error.InternalMessage);
                return Task.FromResult(Result.FromError(error));
            }

            return Task.FromResult(Result.Success);
        }
    }
}
