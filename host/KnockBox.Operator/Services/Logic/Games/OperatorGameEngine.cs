using KnockBox.Core.Primitives.Returns;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.Projection;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace KnockBox.Operator.Services.Logic.Games;

public class OperatorGameEngine(
    ILogger<OperatorGameEngine> logger,
    ILogger<OperatorGameState> stateLogger,
    IRandomNumberService randomNumberService)
    : AbstractGameEngine<OperatorGameState>(minPlayerCount: 2, maxPlayerCount: int.MaxValue),
      IGameStateProjector,
      IGameCommandHandler,
      IServerTickHandler
{
    private readonly OperatorStateProjector _projector = new();

    // Match the hub's wire format: enums as strings, case-insensitive property names,
    // so a client-serialized command payload deserializes here.
    private static readonly JsonSerializerOptions CommandJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    // ── Hub projection / command / tick surface ──────────────────────────────

    /// <summary>Per-recipient projection entry point used by the host's <c>GameViewCoordinator</c>.</summary>
    public object? ProjectFor(AbstractGameState state, Guid recipientId)
        => ((IGameStateProjector)_projector).ProjectFor(state, recipientId);

    /// <summary>Server-owned clock entry point; drives the FSM's time-based transitions.</summary>
    void IServerTickHandler.Tick(AbstractGameState state, DateTimeOffset now)
    {
        if (state is OperatorGameState s && s.Context is not null)
            Tick(s.Context, now);
    }

    /// <summary>
    /// Maps a hub command name to the same engine path a Razor page used to call directly.
    /// Player commands carry the server-resolved <paramref name="caller"/>'s id into the
    /// FSM command (the client can't spoof another player); per-command player gating
    /// (whose turn / reaction target) lives in the FSM states. Host-only commands are
    /// gated here.
    /// </summary>
    public async ValueTask<Result> HandleCommandAsync(
        User caller, AbstractGameState state, string command, string? payloadJson, CancellationToken ct = default)
    {
        if (state is not OperatorGameState s)
            return Result.FromError("Invalid game state for Operator.");

        return command switch
        {
            OperatorCommands.Start             => await StartFromPayload(caller, s, payloadJson, ct),
            OperatorCommands.SubmitSetupChoice => await SetupChoiceFromPayload(caller, s, payloadJson),
            OperatorCommands.PlayCards         => await PlayCardsFromPayload(caller, s, payloadJson),
            OperatorCommands.EndTurn           => await ExecuteCommandAsync(s, new EndTurnCommand(caller.Id)),
            OperatorCommands.SkipTurn          => await ExecuteCommandAsync(s, new SkipTurnCommand(caller.Id)),
            OperatorCommands.PlayReaction      => await PlayReactionFromPayload(caller, s, payloadJson),
            OperatorCommands.PassReaction      => await ExecuteCommandAsync(s, new PassReactionCommand(caller.Id)),
            OperatorCommands.RedirectHotPotato => await RedirectFromPayload(caller, s, payloadJson),
            OperatorCommands.UpdateSettings    => UpdateSettingsFromPayload(caller, s, payloadJson),
            OperatorCommands.KickPlayer        => KickFromPayload(caller, s, payloadJson),
            OperatorCommands.ReturnToLobby     => ReturnToLobby(caller, s),
            _ => Result.FromError($"Unknown command [{command}].")
        };
    }

    // ── Command payload adapters ─────────────────────────────────────────────

    private async Task<Result> StartFromPayload(User caller, OperatorGameState state, string? payloadJson, CancellationToken ct)
    {
        // The start buttons choose whether the host plays; carry it into settings before
        // the host-checked StartAsync runs StartAsyncCore (mirrors CardCounter).
        var payload = Deserialize<StartPayload>(payloadJson);
        if (caller.Id == state.Host.Id)
            state.UpdateSettings(cfg => cfg with { HostPlays = payload?.HostPlays ?? false });
        return await StartAsync(caller, state, ct);
    }

    private async Task<Result> SetupChoiceFromPayload(User caller, OperatorGameState state, string? payloadJson)
    {
        if (Deserialize<SetupChoicePayload>(payloadJson) is not { } p)
            return Result.FromError("Malformed setup-choice payload.");
        // The actual ± value is server-held; the wire carries only the sign.
        decimal choice = p.IsNegative ? state.Settings.InitialPointsNegative : state.Settings.InitialPointsPositive;
        return await ExecuteCommandAsync(state, new SubmitSetupChoiceCommand(caller.Id, choice));
    }

    private async Task<Result> PlayCardsFromPayload(User caller, OperatorGameState state, string? payloadJson)
    {
        if (Deserialize<PlayCardsPayload>(payloadJson) is not { } p)
            return Result.FromError("Malformed play-cards payload.");
        return await ExecuteCommandAsync(state, new PlayCardsCommand(caller.Id, [.. p.CardIds], p.TargetPlayerId));
    }

    private async Task<Result> PlayReactionFromPayload(User caller, OperatorGameState state, string? payloadJson)
    {
        if (Deserialize<PlayReactionPayload>(payloadJson) is not { } p)
            return Result.FromError("Malformed reaction payload.");
        return await ExecuteCommandAsync(state, new PlayReactionCommand(caller.Id, p.ShieldCardId));
    }

    private async Task<Result> RedirectFromPayload(User caller, OperatorGameState state, string? payloadJson)
    {
        if (Deserialize<RedirectPayload>(payloadJson) is not { } p)
            return Result.FromError("Malformed redirect payload.");
        return await ExecuteCommandAsync(state, new RedirectHotPotatoCommand(caller.Id, p.HotPotatoCardId, p.NewTargetPlayerId));
    }

    private Result UpdateSettingsFromPayload(User caller, OperatorGameState state, string? payloadJson)
    {
        // Host-only, and only meaningful before the game starts (the settings drawer is a
        // lobby control). HostPlays is owned by the start buttons, so it (and every other
        // non-surfaced field) is preserved by OperatorSettingsMapping.Apply.
        if (caller.Id != state.Host.Id)
            return Result.FromError("Only the host can change the settings.");
        if (!state.IsJoinable)
            return Result.FromError("Settings can only change before the game starts.");
        if (Deserialize<OperatorSettingsView>(payloadJson) is not { } view)
            return Result.FromError("Malformed settings payload.");
        return state.UpdateSettings(cur => OperatorSettingsMapping.Apply(cur, view));
    }

    private Result KickFromPayload(User caller, OperatorGameState state, string? payloadJson)
    {
        if (Deserialize<KickPayload>(payloadJson) is not { } p)
            return Result.FromError("Malformed kick payload.");

        var target = state.Players.FirstOrDefault(e => e.User.Id == p.PlayerId).User;
        if (target is null)
            return Result.FromError("Player is not in this lobby.");

        return state.KickPlayer(caller, target);
    }

    private static T? Deserialize<T>(string? payloadJson)
    {
        if (string.IsNullOrEmpty(payloadJson)) return default;
        try { return JsonSerializer.Deserialize<T>(payloadJson, CommandJsonOptions); }
        catch (JsonException) { return default; }
    }

    /// <summary>
    /// Operator counts the host as a participant when <see cref="OperatorSettings.HostPlays"/>
    /// is on, so readiness is gated on <see cref="AbstractGameState.Participants"/>.<c>Length</c>
    /// rather than the base check's <c>Players.Length</c>. (Start gating is enforced by the
    /// lobby button; this keeps the readiness API correct for any caller that consults it.)
    /// </summary>
    public override Task<bool> CanStartAsync(AbstractGameState state, CancellationToken ct = default)
    {
        int count = state.Participants.Length;
        bool valid = MinPlayerCount <= count && count <= MaxPlayerCount && state.IsJoinable;
        return Task.FromResult(valid);
    }

    public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
    {
        if (host is null)
            return Task.FromResult(ValueResult<AbstractGameState>.FromError("Failed to create game state.", "Host was null."));

        var state = new OperatorGameState(host, stateLogger);
        state.Context = new OperatorGameContext(state, randomNumberService);
        state.Execute(() => state.SetJoinable(true));
        logger.LogInformation("Created Operator gameState with user [{userId}] as host.", host.Id);
        return Task.FromResult(ValueResult<AbstractGameState>.FromValue(state));
    }

    protected override async Task<Result> StartAsyncCore(OperatorGameState operatorState, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Starting Operator game hosted by user [{hostId}] with {playerCount} player(s).",
            operatorState.Host.Id,
            operatorState.Players.Length);

        var context = new OperatorGameContext(operatorState, randomNumberService);
        var fsm = new FiniteStateMachine<OperatorGameContext, OperatorCommand>(stateLogger);
        context.Fsm = fsm;

        return await operatorState.ExecuteAsync(() =>
        {
            // Fix the host's participation from settings at start time so the snapshot below
            // is self-contained regardless of button ordering. When HostPlays is false,
            // Participants == Players and behavior is unchanged.
            operatorState.SetHostIsParticipant(operatorState.Settings.HostPlays);

            var allParticipants = operatorState.Participants.ToList();

            // Initialize GamePlayers (deck generation and dealing happen in SetupState after choices)
            foreach (var entry in allParticipants)
            {
                var playerState = new OperatorPlayerState { UserId = entry.User.Id };
                operatorState.GamePlayers[entry.User.Id] = playerState;
            }

            // 3. Set Phase to Setup
            operatorState.Phase = OperatorGamePhase.Setup;
            operatorState.Context = context;
            fsm.TransitionTo(context, new KnockBox.Operator.Services.Logic.FSM.States.SetupState());

            // 4. Initialize Turn Manager
            operatorState.TurnManager.SetTurnOrder(allParticipants.Select(p => p.User.Id));

            // 5. Update Joinable Status
            operatorState.SetJoinable(false);

            return ValueTask.CompletedTask;
        }, ct);
    }

    /// <summary>
    /// Returns the game to the lobby (host-only, terminal-phase-only) via the base
    /// <see cref="AbstractGameEngine{TState}.ReturnToLobby"/>. Flipping the state back to
    /// joinable re-renders every player's page at the lobby — no navigation needed.
    /// </summary>
    protected override bool IsTerminalPhase(OperatorGameState state) => state.Phase == OperatorGamePhase.GameOver;

    /// <inheritdoc />
    protected override void ResetForLobby(OperatorGameState state)
    {
        // Fresh context mirrors CreateStateAsync; StartAsyncCore replaces it again on
        // the next start. Keeps the lobby's pre-start invariant (non-null Context).
        state.Context = new OperatorGameContext(state, randomNumberService);
        state.GamePlayers.Clear();
        state.Deck = [];
        state.DiscardPile = [];
        state.ActionLog = [];
        state.LastBlockedActionMessage = null;
        state.BlockedAttackerId = null;
        state.TurnManager.SetTurnOrder([]);
        state.PendingGameActionCommand = null;
        state.ReactionTargetPlayerIds = [];
        state.PlayerReactions = [];
        state.TurnCount = 0;
        state.WinnerPlayerId = null;
        state.Phase = OperatorGamePhase.Setup;
    }

    /// <summary>
    /// Processes a game command by delegating to the current FSM state.
    /// </summary>
    public Task<Result> ExecuteCommandAsync(OperatorGameState state, OperatorCommand command)
    {
        if (state.Context?.Fsm == null)
            return Task.FromResult(Result.FromError("FSM not initialized."));

        var result = state.Execute(() =>
        {
            var fsmResult = state.Context.Fsm.HandleCommand(state.Context, command);
            if (fsmResult.TryGetFailure(out var err))
            {
                return Result.FromError(err.PublicMessage, err.InternalMessage);
            }
            return Result.Success;
        });

        if (!result.IsSuccess) return Task.FromResult<Result>(result.Error.Error);
        return Task.FromResult(result.Value);
    }

    /// <summary>
    /// Drives time-based transitions.
    /// </summary>
    public Result Tick(OperatorGameContext context, DateTimeOffset now)
    {
        if (context.Fsm == null) return Result.Success;

        var executeResult = context.State.Execute(() =>
        {
            var fsmResult = context.Fsm.Tick(context, now);
            if (fsmResult.TryGetFailure(out var err))
            {
                return Result.FromError(err.PublicMessage, err.InternalMessage);
            }
            return Result.Success;
        });

        if (!executeResult.IsSuccess) return executeResult.Error.Error;
        return executeResult.Value;
    }
}
