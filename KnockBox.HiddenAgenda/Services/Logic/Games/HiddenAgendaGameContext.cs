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

            var drawn = TaskPool.DrawTasks(Rng, availablePool, 3);
            playerState.SecretTasks = drawn;

            // If R6 is drawn, assign random rival target
            if (drawn.Any(t => t.Id == "R6"))
            {
                var otherPlayers = GamePlayers.Keys.Where(id => id != playerId).ToList();
                if (otherPlayers.Count > 0)
                {
                    playerState.RivalryTargetPlayerId = otherPlayers[Rng.GetRandomInt(0, otherPlayers.Count)];
                }
            }
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

        /// <summary>
        /// Records a card play in both the player's history and the global round history.
        /// Determines the EffectiveCardType from applied effects (Acquire if all positive,
        /// Remove if all negative, Trade if mixed) and snapshots current collection progress
        /// for R4/R5 task evaluation.
        /// </summary>
        public void RecordCardPlay(string playerId, CurationCard card, int selectedIndex,
            IReadOnlyList<CurationCard> allDrawn, IReadOnlyList<CollectionEffect> appliedEffects)
        {
            var player = GamePlayers[playerId];
            var affected = appliedEffects.Select(e => e.Collection).Distinct().ToArray();

            // Determine effective type from applied effects
            bool hasPositive = appliedEffects.Any(e => e.Delta > 0);
            bool hasNegative = appliedEffects.Any(e => e.Delta < 0);
            var effectiveType = (hasPositive, hasNegative) switch
            {
                (true, false) => CurationCardType.Acquire,
                (false, true) => CurationCardType.Remove,
                _ => CurationCardType.Trade  // mixed or no effects
            };

            // Snapshot collection progress at time of play (for R4/R5 evaluation)
            var progressSnapshot = new Dictionary<CollectionType, int>(CollectionProgress);

            var record = new CardPlayRecord(
                player.TurnsTakenThisRound + 1,
                card,
                selectedIndex,
                affected,
                card.Type,
                effectiveType,
                progressSnapshot);
            player.CardPlayHistory.Add(record);

            var space = Board.Spaces[player.CurrentSpaceId];
            State.RoundPlayHistory.Add(new TurnRecord(
                State.TotalTurnsTaken + 1,
                playerId,
                record,
                player.CurrentSpaceId,
                space.Wing));
        }

        /// <summary>
        /// Evaluates whether a specific task was completed by a player this round.
        /// </summary>
        public bool EvaluateTaskCompletion(string playerId, SecretTask task)
        {
            return task.Id switch
            {
                "D1" => EvaluateDevotionCollection(playerId, CollectionType.RenaissanceMasters, 4),
                "D2" => EvaluateDevotionCollection(playerId, CollectionType.ContemporaryShowcase, 4),
                "D3" => EvaluateDevotionCollection(playerId, CollectionType.ImpressionistGallery, 4),
                "D4" => EvaluateDevotionCollection(playerId, CollectionType.MarbleAndBronze, 4),
                "D5" => EvaluateDevotionCollection(playerId, CollectionType.EmergingArtists, 4),
                "D6" => EvaluateDevotionWing(playerId, Wing.GrandHall, 5),
                "D7" => EvaluateDevotionWing(playerId, Wing.SculptureGarden, 5),
                "N1" => EvaluateNeglectCollection(playerId, CollectionType.RenaissanceMasters),
                "N2" => EvaluateNeglectCollection(playerId, CollectionType.ContemporaryShowcase),
                "N3" => EvaluateNeglectCollection(playerId, CollectionType.ImpressionistGallery),
                "N4" => EvaluateNeglectWing(playerId, Wing.GrandHall),
                "N5" => EvaluateNeglectWing(playerId, Wing.ModernWing),
                "N6" => EvaluateNeglectCardType(playerId, CurationCardType.Remove),
                "Y1" => EvaluateStyleRemoveCount(playerId, 3),
                "Y2" => EvaluateStyleCollectionVariety(playerId, 4),
                "Y3" => EvaluateStyleConsecutiveCollection(playerId, 3),
                "Y4" => EvaluateStyleAlternating(playerId, 4),
                "Y5" => EvaluateStyleHighestValue(playerId, 4),
                "Y6" => EvaluateStyleEventSpotVisits(playerId, 3),
                "M1" => EvaluateMovementAllWings(playerId),
                "M2" => EvaluateMovementCamping(playerId, 4),
                "M3" => EvaluateMovementSameSpot(playerId, 3),
                "M4" => EvaluateMovementFullDistance(playerId, 4),
                "M5" => EvaluateMovementWingHopping(playerId, 4),
                "M6" => EvaluateMovementRevisit(playerId, 3),
                "R1" => EvaluateRivalryRescue(playerId, 3),
                "R2" => EvaluateRivalryEcho(playerId, 4),
                "R3" => EvaluateRivalryAvoid(playerId, 5),
                "R4" => EvaluateRivalryTopRemove(playerId, 3),
                "R5" => EvaluateRivalryBottomAcquire(playerId, 3),
                "R6" => EvaluateRivalryShadow(playerId, 4),
                _ => false
            };
        }

        // Devotion: count turns where player's card play added progress to specified collection
        private bool EvaluateDevotionCollection(string playerId, CollectionType collection, int threshold)
        {
            var player = GamePlayers[playerId];
            int count = player.CardPlayHistory
                .Where(r => r.CardType == CurationCardType.Acquire
                          && r.AffectedCollections.Contains(collection))
                .Select(r => r.TurnNumber)
                .Distinct()
                .Count();
            return count >= threshold;
        }

        // Devotion (wing): count turns affecting any collection in the specified wing
        private bool EvaluateDevotionWing(string playerId, Wing wing, int threshold)
        {
            var wingCollections = CollectionDefinitions.All
                .Where(c => c.PrimaryWing == wing)
                .Select(c => c.Type)
                .ToHashSet();
            var player = GamePlayers[playerId];
            int count = player.CardPlayHistory
                .Where(r => r.AffectedCollections.Any(c => wingCollections.Contains(c)))
                .Select(r => r.TurnNumber)
                .Distinct()
                .Count();
            return count >= threshold;
        }

        // Neglect: verify player never played Acquire on specified collection
        private bool EvaluateNeglectCollection(string playerId, CollectionType collection)
        {
            var player = GamePlayers[playerId];
            return !player.CardPlayHistory.Any(r =>
                r.CardType == CurationCardType.Acquire
                && r.AffectedCollections.Contains(collection));
        }

        // Neglect (wing): verify player never entered specified wing
        private bool EvaluateNeglectWing(string playerId, Wing wing)
        {
            var player = GamePlayers[playerId];
            return !player.MovementHistory.Any(m => m.Wing == wing);
        }

        // Neglect (card type): verify player never played specified card type
        private bool EvaluateNeglectCardType(string playerId, CurationCardType type)
        {
            var player = GamePlayers[playerId];
            return !player.CardPlayHistory.Any(r => r.CardType == type);
        }

        // Style Y1: Remove card on >= N turns
        private bool EvaluateStyleRemoveCount(string playerId, int threshold)
        {
            var player = GamePlayers[playerId];
            int count = player.CardPlayHistory
                .Where(r => r.CardType == CurationCardType.Remove)
                .Select(r => r.TurnNumber)
                .Distinct()
                .Count();
            return count >= threshold;
        }

        // Style Y2: cards affecting >= N different collections
        private bool EvaluateStyleCollectionVariety(string playerId, int threshold)
        {
            var player = GamePlayers[playerId];
            var distinct = player.CardPlayHistory
                .SelectMany(r => r.AffectedCollections)
                .Distinct()
                .Count();
            return distinct >= threshold;
        }

        // Style Y3: same collection >= N turns in a row
        private bool EvaluateStyleConsecutiveCollection(string playerId, int threshold)
        {
            var player = GamePlayers[playerId];
            var ordered = player.CardPlayHistory.OrderBy(r => r.TurnNumber).ToList();
            if (ordered.Count < threshold) return false;

            var allCollections = ordered.SelectMany(r => r.AffectedCollections).Distinct();
            foreach (var collection in allCollections)
            {
                int maxStreak = 0;
                int currentStreak = 0;
                int lastTurn = -1;
                foreach (var record in ordered)
                {
                    if (record.AffectedCollections.Contains(collection))
                    {
                        if (lastTurn == -1 || record.TurnNumber == lastTurn + 1)
                            currentStreak++;
                        else
                            currentStreak = 1;
                        lastTurn = record.TurnNumber;
                        maxStreak = Math.Max(maxStreak, currentStreak);
                    }
                    else
                    {
                        currentStreak = 0;
                        lastTurn = -1;
                    }
                }
                if (maxStreak >= threshold) return true;
            }
            return false;
        }

        // Style Y4: alternate Acquire/Remove for >= N consecutive turns
        private bool EvaluateStyleAlternating(string playerId, int threshold)
        {
            var player = GamePlayers[playerId];
            var ordered = player.CardPlayHistory
                .OrderBy(r => r.TurnNumber)
                .ToList();

            if (ordered.Count < threshold) return false;

            int maxStreak = 1;
            int currentStreak = 1;
            for (int i = 1; i < ordered.Count; i++)
            {
                var prevType = ordered[i - 1].EffectiveCardType;
                var currType = ordered[i].EffectiveCardType;

                if ((prevType == CurationCardType.Acquire && currType == CurationCardType.Remove) ||
                    (prevType == CurationCardType.Remove && currType == CurationCardType.Acquire))
                {
                    currentStreak++;
                    maxStreak = Math.Max(maxStreak, currentStreak);
                }
                else
                {
                    currentStreak = 1;
                }
            }
            return maxStreak >= threshold;
        }

        // Style Y5: play the highest-value card in hand >= N turns
        private bool EvaluateStyleHighestValue(string playerId, int threshold)
        {
            var player = GamePlayers[playerId];
            int count = 0;

            foreach (var draw in player.CardDrawHistory)
            {
                var play = player.CardPlayHistory.FirstOrDefault(p => p.TurnNumber == draw.TurnNumber);
                if (play == null) continue;

                var cardValues = draw.DrawnCards.Select(GetCardValue).ToList();
                var maxValue = cardValues.Max();
                var selectedValue = GetCardValue(play.Card);

                if (selectedValue >= maxValue)
                {
                    count++;
                }
            }

            return count >= threshold;
        }

        private int GetCardValue(CurationCard card)
        {
            int effectsValue = card.Effects.Sum(e => Math.Abs(e.Delta));
            if (card.Type == CurationCardType.Trade && card.AlternateEffects != null)
            {
                int altValue = card.AlternateEffects.Sum(e => Math.Abs(e.Delta));
                return Math.Max(effectsValue, altValue);
            }
            return effectsValue;
        }

        // Style Y6: visit an Event Spot >= N times
        private bool EvaluateStyleEventSpotVisits(string playerId, int threshold)
        {
            var player = GamePlayers[playerId];
            int count = player.MovementHistory
                .Select(m => Board.Spaces[m.SpaceId])
                .Count(s => s.SpotType == SpotType.Event);
            return count >= threshold;
        }

        // Movement M1: visit all 4 wings
        private bool EvaluateMovementAllWings(string playerId)
        {
            var player = GamePlayers[playerId];
            var visited = player.MovementHistory
                .Select(m => m.Wing)
                .Where(w => w != Wing.Corridor)
                .Distinct()
                .Count();
            return visited >= 4;
        }

        // Movement M2: >= N turns in same wing
        private bool EvaluateMovementCamping(string playerId, int threshold)
        {
            var player = GamePlayers[playerId];
            return player.MovementHistory
                .Where(m => m.Wing != Wing.Corridor)
                .GroupBy(m => m.Wing)
                .Any(g => g.Count() >= threshold);
        }

        // Movement M3: same spot as another player >= N times
        private bool EvaluateMovementSameSpot(string playerId, int threshold)
        {
            var player = GamePlayers[playerId];
            int count = 0;
            var myMoves = State.RoundPlayHistory.Where(r => r.PlayerId == playerId).ToList();

            foreach (var turn in myMoves)
            {
                foreach (var otherId in GamePlayers.Keys.Where(id => id != playerId))
                {
                    // Find the other player's position at the time of this global turn.
                    // This is their last recorded position at or before this global turn.
                    var otherPos = State.RoundPlayHistory
                        .Where(r => r.PlayerId == otherId && r.TurnNumber <= turn.TurnNumber)
                        .OrderByDescending(r => r.TurnNumber)
                        .FirstOrDefault();
                    
                    if (otherPos != null && otherPos.SpaceId == turn.SpaceId)
                    {
                        count++;
                        break; // Count once per turn
                    }
                }
            }
            return count >= threshold;
        }

        // Movement M4: full distance on >= N consecutive turns
        private bool EvaluateMovementFullDistance(string playerId, int threshold)
        {
            var player = GamePlayers[playerId];
            var ordered = player.MovementHistory.OrderBy(m => m.TurnNumber).ToList();
            if (ordered.Count < threshold) return false;

            int maxStreak = 0;
            int currentStreak = 0;
            int? prevSpaceId = null;

            foreach (var move in ordered)
            {
                if (prevSpaceId.HasValue)
                {
                    int dist = Board.GetShortestDistance(prevSpaceId.Value, move.SpaceId);
                    if (dist == move.SpinResult)
                    {
                        currentStreak++;
                    }
                    else
                    {
                        currentStreak = 0;
                    }
                    maxStreak = Math.Max(maxStreak, currentStreak);
                }
                else
                {
                    // For the very first move of the round, we don't have a prevSpaceId in MovementHistory,
                    // but we might be able to find it in the global history if there were previous rounds,
                    // or just skip it as a streak starter for simplicity in this round's context.
                    // The spec says "on at least 4 consecutive turns", and we start with an empty history each round.
                }
                prevSpaceId = move.SpaceId;
            }

            return maxStreak >= threshold;
        }

        // Movement M5: change wings every turn for >= N consecutive turns
        private bool EvaluateMovementWingHopping(string playerId, int threshold)
        {
            var player = GamePlayers[playerId];
            var ordered = player.MovementHistory.OrderBy(m => m.TurnNumber).ToList();
            if (ordered.Count < threshold) return false;

            int maxStreak = 1;
            int currentStreak = 1;
            for (int i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].Wing != ordered[i - 1].Wing)
                {
                    currentStreak++;
                    maxStreak = Math.Max(maxStreak, currentStreak);
                }
                else
                {
                    currentStreak = 1;
                }
            }
            return maxStreak >= threshold;
        }

        // Movement M6: return to same spot >= N times
        private bool EvaluateMovementRevisit(string playerId, int threshold)
        {
            var player = GamePlayers[playerId];
            return player.MovementHistory
                .GroupBy(m => m.SpaceId)
                .Any(g => g.Count() >= threshold);
        }

        // Rivalry R1: Acquire after another's Remove on same collection, >= N times
        private bool EvaluateRivalryRescue(string playerId, int threshold)
        {
            var player = GamePlayers[playerId];
            int count = 0;
            var myTurns = State.RoundPlayHistory.Where(r => r.PlayerId == playerId && r.CardPlay != null).ToList();

            foreach (var turn in myTurns)
            {
                if (turn.CardPlay!.CardType != CurationCardType.Acquire) continue;

                var prevTurn = State.RoundPlayHistory
                    .FirstOrDefault(r => r.TurnNumber == turn.TurnNumber - 1);
                
                if (prevTurn != null && prevTurn.PlayerId != playerId && prevTurn.CardPlay != null
                    && prevTurn.CardPlay.CardType == CurationCardType.Remove
                    && prevTurn.CardPlay.AffectedCollections.Intersect(turn.CardPlay.AffectedCollections).Any())
                {
                    count++;
                }
            }
            return count >= threshold;
        }

        // Rivalry R2: same collection as player immediately before you, >= N turns
        private bool EvaluateRivalryEcho(string playerId, int threshold)
        {
            var turnOrder = State.TurnManager.TurnOrder;
            int myIndex = turnOrder.IndexOf(playerId);
            if (myIndex == -1) return false;
            int prevIndex = (myIndex - 1 + turnOrder.Count) % turnOrder.Count;
            string prevPlayerId = turnOrder[prevIndex];

            int count = 0;
            var myTurns = State.RoundPlayHistory
                .Where(r => r.PlayerId == playerId && r.CardPlay != null)
                .OrderBy(r => r.TurnNumber);

            foreach (var myTurn in myTurns)
            {
                var prevPlayerTurn = State.RoundPlayHistory
                    .Where(r => r.PlayerId == prevPlayerId && r.CardPlay != null && r.TurnNumber < myTurn.TurnNumber)
                    .OrderByDescending(r => r.TurnNumber)
                    .FirstOrDefault();

                if (prevPlayerTurn?.CardPlay != null
                    && myTurn.CardPlay!.AffectedCollections
                        .Intersect(prevPlayerTurn.CardPlay.AffectedCollections).Any())
                {
                    count++;
                }
            }
            return count >= threshold;
        }

        // Rivalry R3: never affect same collection as player immediately before you, >= N consecutive turns
        private bool EvaluateRivalryAvoid(string playerId, int threshold)
        {
            var turnOrder = State.TurnManager.TurnOrder;
            int myIndex = turnOrder.IndexOf(playerId);
            if (myIndex == -1) return false;
            int prevIndex = (myIndex - 1 + turnOrder.Count) % turnOrder.Count;
            string prevPlayerId = turnOrder[prevIndex];

            var myTurns = State.RoundPlayHistory
                .Where(r => r.PlayerId == playerId && r.CardPlay != null)
                .OrderBy(r => r.TurnNumber)
                .ToList();

            int maxStreak = 0;
            int currentStreak = 0;
            foreach (var myTurn in myTurns)
            {
                var prevPlayerTurn = State.RoundPlayHistory
                    .Where(r => r.PlayerId == prevPlayerId && r.CardPlay != null && r.TurnNumber < myTurn.TurnNumber)
                    .OrderByDescending(r => r.TurnNumber)
                    .FirstOrDefault();

                if (prevPlayerTurn?.CardPlay == null)
                {
                    currentStreak++; 
                }
                else
                {
                    bool overlaps = myTurn.CardPlay!.AffectedCollections
                        .Intersect(prevPlayerTurn.CardPlay.AffectedCollections).Any();
                    if (!overlaps)
                        currentStreak++;
                    else
                        currentStreak = 0;
                }
                maxStreak = Math.Max(maxStreak, currentStreak);
            }
            return maxStreak >= threshold;
        }

        // Rivalry R4: play Remove on highest-progress collection >= N times
        private bool EvaluateRivalryTopRemove(string playerId, int threshold)
        {
            var player = GamePlayers[playerId];
            int count = 0;
            foreach (var play in player.CardPlayHistory.Where(r => r.EffectiveCardType == CurationCardType.Remove))
            {
                if (play.CollectionProgressSnapshot == null || play.CollectionProgressSnapshot.Count == 0) continue;
                var maxProgress = play.CollectionProgressSnapshot.Values.Max();
                var highestCollections = play.CollectionProgressSnapshot
                    .Where(kv => kv.Value == maxProgress)
                    .Select(kv => kv.Key)
                    .ToHashSet();
                if (play.AffectedCollections.Any(c => highestCollections.Contains(c)))
                    count++;
            }
            return count >= threshold;
        }

        // Rivalry R5: play Acquire on lowest-progress collection >= N times
        private bool EvaluateRivalryBottomAcquire(string playerId, int threshold)
        {
            var player = GamePlayers[playerId];
            int count = 0;
            foreach (var play in player.CardPlayHistory.Where(r => r.EffectiveCardType == CurationCardType.Acquire))
            {
                if (play.CollectionProgressSnapshot == null || play.CollectionProgressSnapshot.Count == 0) continue;
                var minProgress = play.CollectionProgressSnapshot.Values.Min();
                var lowestCollections = play.CollectionProgressSnapshot
                    .Where(kv => kv.Value == minProgress)
                    .Select(kv => kv.Key)
                    .ToHashSet();
                if (play.AffectedCollections.Any(c => lowestCollections.Contains(c)))
                    count++;
            }
            return count >= threshold;
        }

        // Rivalry R6: same wing as randomly assigned target player >= N turns
        private bool EvaluateRivalryShadow(string playerId, int threshold)
        {
            var player = GamePlayers[playerId];
            if (player.RivalryTargetPlayerId == null) return false;
            if (!GamePlayers.TryGetValue(player.RivalryTargetPlayerId, out var target)) return false;

            int count = 0;
            var myMoves = State.RoundPlayHistory.Where(r => r.PlayerId == playerId).ToList();

            foreach (var turn in myMoves)
            {
                var targetPos = State.RoundPlayHistory
                    .Where(r => r.PlayerId == target.PlayerId && r.TurnNumber <= turn.TurnNumber)
                    .OrderByDescending(r => r.TurnNumber)
                    .FirstOrDefault();
                
                if (targetPos != null && targetPos.Wing == turn.Wing)
                    count++;
            }
            return count >= threshold;
        }
    }
}
