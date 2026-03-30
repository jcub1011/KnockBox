using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Timed phase in which every player draws clothing items of the current type before
    /// the clock expires.  One instance of this state is used per clothing type; when the
    /// timer (or all-ready shortcut) fires the state either chains to the next
    /// <see cref="DrawingRoundState"/> (next clothing type) or to <see cref="PoolRevealState"/>
    /// when all types have been completed.
    ///
    /// Transition ownership:
    /// - Timer expiry (last type) → <see cref="PoolRevealState"/>
    /// - Timer expiry (not last type) → next <see cref="DrawingRoundState"/>
    /// - All players mark ready early → same advance logic as timer expiry
    /// - <see cref="SubmitDrawingCommand"/> → stored; no transition until all ready or timer fires
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// - <see cref="AbandonGameCommand"/> (host only) → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class DrawingRoundState : ITimedDrawnToDressGameState
    {
        public bool IsTimerOptional => true;

        private readonly int _clothingTypeIndex;
        private DateTimeOffset _deadline;

        /// <summary>
        /// Initialises the drawing round for the specified clothing-type slot.
        /// </summary>
        /// <param name="clothingTypeIndex">
        /// 0-based index into <see cref="DrawnToDressConfig.ClothingTypes"/> for the type
        /// players will draw during this round.  Defaults to <c>0</c> (first type).
        /// </param>
        public DrawingRoundState(int clothingTypeIndex = 0)
        {
            _clothingTypeIndex = clothingTypeIndex;
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            _deadline = DateTimeOffset.UtcNow.AddSeconds(context.Config.DrawingTimeSec);
            context.State.PhaseDeadlineUtc = _deadline;
            context.State.CurrentDrawingClothingTypeIndex = _clothingTypeIndex;
            context.State.SetPhase(GamePhase.Drawing);
            context.ResetReadyFlags();

            var typeName = GetCurrentTypeName(context);
            context.Logger.LogInformation(
                "FSM → DrawingRoundState [{index}] ({type}). Deadline: {deadline}.",
                _clothingTypeIndex, typeName, _deadline);
            return null;
        }

        public Result OnExit(DrawnToDressGameContext context)
        {
            context.State.PhaseDeadlineUtc = null;
            return Result.Success;
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            switch (command)
            {
                case SubmitDrawingCommand cmd:
                    return HandleSubmitDrawing(context, cmd);

                case MarkReadyCommand cmd:
                    return HandleMarkReady(context, cmd);

                case PauseGameCommand:
                    return new PausedState(this);

                case AbandonGameCommand:
                    return new AbandonedState();

                default:
                    context.Logger.LogWarning(
                        "DrawingRoundState: unrecognized command [{type}] from player [{id}].",
                        command.GetType().Name, command.PlayerId);
                    return null;
            }
        }

        public ValueResult<TimeSpan> GetRemainingTime(
            DrawnToDressGameContext context, DateTimeOffset now)
            => _deadline - now;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> Tick(
            DrawnToDressGameContext context, DateTimeOffset now)
        {
            if (now < _deadline) return null;

            var typeName = GetCurrentTypeName(context);
            context.Logger.LogInformation(
                "Drawing timer expired for type [{index}] ({type}).", _clothingTypeIndex, typeName);
            return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(
                AdvanceToNextRound(context));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private string GetCurrentTypeName(DrawnToDressGameContext context)
        {
            var types = context.Config.ClothingTypes;
            if (_clothingTypeIndex < types.Count)
                return types[_clothingTypeIndex].DisplayName;
            return $"[{_clothingTypeIndex}]";
        }

        /// <summary>
        /// Returns the next FSM state: a new <see cref="DrawingRoundState"/> for the next
        /// clothing type, or <see cref="PoolRevealState"/> if all types are done.
        /// </summary>
        private IGameState<DrawnToDressGameContext, DrawnToDressCommand> AdvanceToNextRound(
            DrawnToDressGameContext context)
        {
            int nextIndex = _clothingTypeIndex + 1;
            if (nextIndex < context.Config.ClothingTypes.Count)
            {
                context.Logger.LogInformation(
                    "Advancing to DrawingRoundState [{index}].", nextIndex);
                return new DrawingRoundState(nextIndex);
            }

            context.Logger.LogInformation("All clothing types drawn. Moving to pool reveal.");
            return new PoolRevealState();
        }

        private ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleSubmitDrawing(
            DrawnToDressGameContext context, SubmitDrawingCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "SubmitDrawing: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            // Validate the clothing type matches the CURRENT round.
            var types = context.Config.ClothingTypes;
            if (_clothingTypeIndex >= types.Count)
            {
                context.Logger.LogWarning(
                    "SubmitDrawing: clothing type index [{index}] out of range.", _clothingTypeIndex);
                return null;
            }

            var currentType = types[_clothingTypeIndex];
            if (!string.Equals(cmd.ClothingTypeId, currentType.Id, StringComparison.Ordinal))
            {
                context.Logger.LogWarning(
                    "SubmitDrawing: player [{id}] submitted type [{submitted}] but current round is [{current}].",
                    cmd.PlayerId, cmd.ClothingTypeId, currentType.Id);
                return null;
            }

            // Enforce max-items-per-round when a limit is configured.
            if (currentType.MaxItemsPerRound > 0)
            {
                int alreadySubmitted = context.ClothingPool.Values
                    .Count(item => item.CreatorPlayerId == cmd.PlayerId
                                   && item.ClothingTypeId == currentType.Id);

                if (alreadySubmitted >= currentType.MaxItemsPerRound)
                {
                    context.Logger.LogWarning(
                        "SubmitDrawing: player [{id}] has already submitted the maximum of {max} item(s) for type [{type}].",
                        cmd.PlayerId, currentType.MaxItemsPerRound, currentType.Id);
                    return null;
                }
            }

            // Store the drawn item and place it into the pool.
            var item = new DrawnClothingItem
            {
                ClothingTypeId = cmd.ClothingTypeId,
                CreatorPlayerId = cmd.PlayerId,
                SvgContent = cmd.SvgContent,
                IsInPool = true,
            };
            context.ClothingPool[item.Id] = item;
            player.OwnedClothingItemIds.Add(item.Id);

            context.Logger.LogInformation(
                "Player [{id}] submitted a [{type}] drawing (item {itemId}).",
                cmd.PlayerId, cmd.ClothingTypeId, item.Id);

            return null; // Timer drives the transition.
        }

        private ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleMarkReady(
            DrawnToDressGameContext context, MarkReadyCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "MarkReady: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            player.IsReady = true;
            context.Logger.LogInformation(
                "Player [{id}] marked ready in DrawingRoundState [{index}].",
                cmd.PlayerId, _clothingTypeIndex);

            if (context.AllPlayersReady())
            {
                context.Logger.LogInformation(
                    "All players ready at type [{index}]. Advancing early.", _clothingTypeIndex);
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(
                    AdvanceToNextRound(context));
            }

            return null;
        }
    }
}
