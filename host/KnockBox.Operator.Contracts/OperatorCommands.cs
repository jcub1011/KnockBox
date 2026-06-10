namespace KnockBox.Operator.Contracts;

/// <summary>Command names the client sends to the server engine via the hub.</summary>
public static class OperatorCommands
{
    public const string Start = "start";                    // host starts (HostPlays carried in payload)
    public const string SubmitSetupChoice = "submit-setup-choice";
    public const string PlayCards = "play-cards";
    public const string EndTurn = "end-turn";
    public const string SkipTurn = "skip-turn";
    public const string PlayReaction = "play-reaction";
    public const string PassReaction = "pass-reaction";
    public const string RedirectHotPotato = "redirect-hot-potato";
    public const string UpdateSettings = "update-settings";
    public const string ReturnToLobby = "return-to-lobby";
    public const string KickPlayer = "kick-player";
}

/// <summary>Payload for <see cref="OperatorCommands.Start"/>: whether the host is dealt in.</summary>
public sealed record StartPayload(bool HostPlays);

/// <summary>
/// Payload for <see cref="OperatorCommands.SubmitSetupChoice"/>: positive vs negative
/// starting score. The actual ± value lives in server settings (InitialPointsPositive /
/// InitialPointsNegative), so the wire carries only the sign choice.
/// </summary>
public sealed record SetupChoicePayload(bool IsNegative);

/// <summary>Payload for <see cref="OperatorCommands.PlayCards"/>.</summary>
public sealed record PlayCardsPayload(Guid[] CardIds, Guid? TargetPlayerId);

/// <summary>Payload for <see cref="OperatorCommands.PlayReaction"/> (block with a Shield).</summary>
public sealed record PlayReactionPayload(Guid ShieldCardId);

/// <summary>Payload for <see cref="OperatorCommands.RedirectHotPotato"/>.</summary>
public sealed record RedirectPayload(Guid HotPotatoCardId, Guid NewTargetPlayerId);

/// <summary>Payload for <see cref="OperatorCommands.KickPlayer"/>.</summary>
public sealed record KickPayload(Guid PlayerId);
