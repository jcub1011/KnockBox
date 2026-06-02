namespace KnockBox.AlphaChain.Services.Logic.Games.FSM
{
    /// <summary>
    /// Base for all player-issued commands processed by the Alpha Chain FSM. Every
    /// command carries the id of the player that issued it so states can validate
    /// permissions (active-player restrictions, host-only commands, etc.).
    /// </summary>
    public abstract record AlphaChainCommand(string ActorUserId);

    /// <summary>Rotates the active player to the next seat in turn order.</summary>
    public record AdvanceTurnCommand(string ActorUserId) : AlphaChainCommand(ActorUserId);

    /// <summary>
    /// Host-only request to skip the currently-showing tutorial and advance immediately to the
    /// next screen. Handled by <c>TutorialState</c> (full-screen Shiritori/Engine) and by
    /// <c>IntermissionState</c> during its <c>TaxTutorial</c> sub-phase; ignored elsewhere. The
    /// engine and the receiving states both check the actor is the host.
    /// </summary>
    public record SkipTutorialCommand(string ActorUserId) : AlphaChainCommand(ActorUserId);
}
