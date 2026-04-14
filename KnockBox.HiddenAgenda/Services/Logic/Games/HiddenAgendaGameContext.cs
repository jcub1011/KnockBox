using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Interfaces;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.HiddenAgenda.Services.Logic.Games.FSM;
using KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using Microsoft.Extensions.Logging;

namespace KnockBox.HiddenAgenda.Services.Logic.Games
{
    public class HiddenAgendaGameContext(
        HiddenAgendaGameState state,
        IRandomNumberService rng,
        ILogger logger)
    {
        public HiddenAgendaGameState State { get; } = state;
        public IRandomNumberService Rng { get; } = rng;
        public ILogger Logger { get; } = logger;
        public IFiniteStateMachine<HiddenAgendaGameContext, HiddenAgendaCommand> Fsm { get; set; } = null!;

        public ConcurrentDictionary<string, HiddenAgendaPlayerState> GamePlayers => State.GamePlayers;
        public BoardGraph Board => State.BoardGraph;
        public Dictionary<CollectionType, int> CollectionProgress => State.CollectionProgress;

        public enum RoundEndTrigger { None, CollectionTrigger, GuessCountdown, MaxTurns }

        public int SpinSpinner()
        {
            return Rng.GetRandomInt(3, 13); // 3 to 12 inclusive
        }

        public void ApplyCollectionEffects(IReadOnlyList<CollectionEffect> effects)
        {
            foreach (var effect in effects)
            {
                if (!CollectionProgress.ContainsKey(effect.Collection))
                    CollectionProgress[effect.Collection] = 0;

                CollectionProgress[effect.Collection] = Math.Max(0, CollectionProgress[effect.Collection] + effect.Delta);
            }
        }

        public void DrawTasksForPlayer(string playerId)
        {
            if (!GamePlayers.TryGetValue(playerId, out var playerState)) return;

            var pool = State.CurrentTaskPool;
            var assignedTasks = GamePlayers.Values.SelectMany(p => p.SecretTasks).Select(t => t.Id).ToHashSet();
            
            // Filter pool to avoid duplicating already-assigned tasks if possible
            var availablePool = pool.Where(t => !assignedTasks.Contains(t.Id)).ToList();
            if (availablePool.Count < 3) availablePool = pool.ToList(); // Fallback if pool is too small

            playerState.SecretTasks = TaskPool.DrawTasks(Rng, availablePool, 3);
        }

        public int GetCompletedCollectionCount()
        {
            int count = 0;
            foreach (var collection in CollectionDefinitions.All)
            {
                if (CollectionProgress.TryGetValue(collection.Type, out var progress) && progress >= collection.TargetValue)
                {
                    count++;
                }
            }
            return count;
        }

        public int GetMaxTurnsPerPlayer()
        {
            return GamePlayers.Count switch
            {
                3 => 12,
                4 => 11,
                5 => 10,
                6 => 9,
                _ => 12 // Default
            };
        }

        public RoundEndTrigger CheckRoundEndConditions()
        {
            // 1. 3 of 5 collections completed
            if (GetCompletedCollectionCount() >= 3)
                return RoundEndTrigger.CollectionTrigger;

            // 2. Guess countdown expired
            if (State.GuessCountdownActive)
            {
                bool allExpired = true;
                foreach (var player in GamePlayers.Values)
                {
                    if (player.GuessCountdownTurnsRemaining > 0)
                    {
                        allExpired = false;
                        break;
                    }
                }
                if (allExpired) return RoundEndTrigger.GuessCountdown;
            }

            // 3. Max turns
            int maxTurns = GetMaxTurnsPerPlayer();
            bool allAtMax = GamePlayers.Count > 0;
            foreach (var player in GamePlayers.Values)
            {
                if (player.TurnsTakenThisRound < maxTurns)
                {
                    allAtMax = false;
                    break;
                }
            }
            if (allAtMax) return RoundEndTrigger.MaxTurns;

            return RoundEndTrigger.None;
        }

        public void ResetForNewRound()
        {
            State.CurrentRound++;
            State.TotalTurnsTaken = 0;
            State.GuessCountdownActive = false;
            State.FirstGuessPlayerId = null;
            State.RoundPlayHistory.Clear();

            // Reset collections
            CollectionProgress.Clear();
            foreach (var type in Enum.GetValues<CollectionType>())
            {
                CollectionProgress[type] = 0;
            }

            // Rotate task pool
            int poolSize = GamePlayers.Count <= 3 ? 25 : 30;
            State.CurrentTaskPool = State.Config.PoolRotation switch
            {
                TaskPoolRotation.Full => TaskPool.GetPoolForPlayerCount(GamePlayers.Count),
                TaskPoolRotation.Partial => TaskPool.AllTasks.OrderBy(_ => Rng.GetRandomInt(10000)).Take(poolSize).ToList(),
                _ => State.CurrentTaskPool.Count > 0 ? State.CurrentTaskPool : TaskPool.GetPoolForPlayerCount(GamePlayers.Count)
            };

            // Reset player round state
            foreach (var player in GamePlayers.Values)
            {
                player.SecretTasks.Clear();
                player.HeldEventCard = null;
                player.DetourPending = false;
                player.DetourTargetPlayerId = null;
                player.HasSubmittedGuess = false;
                player.GuessSubmission = null;
                player.RoundScore = 0;
                player.TurnsTakenThisRound = 0;
                player.GuessCountdownTurnsRemaining = 0;
                player.LastSpinResult = 0;
                player.LastMoveDestination = null;
                player.MovementHistory.Clear();
                player.CardPlayHistory.Clear();
                player.CardDrawHistory.Clear();
                
                // Re-draw tasks
                DrawTasksForPlayer(player.PlayerId);
            }
        }

        public IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>? AdvanceToNextPlayerOrEndRound()
        {
            var trigger = CheckRoundEndConditions();
            if (trigger != RoundEndTrigger.None)
                return new FinalGuessState();

            State.TurnManager.NextTurn();
            return new EventCardPhaseState();
        }
    }
}
