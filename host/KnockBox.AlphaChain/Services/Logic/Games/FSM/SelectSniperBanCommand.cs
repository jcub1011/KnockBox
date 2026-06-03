namespace KnockBox.AlphaChain.Services.Logic.Games.FSM
{
    /// <summary>
    /// The last-place player's pick of the next era's banned letter during the Intermission's
    /// Sniper Ban sub-phase. Only the resolved <c>SniperBanUserId</c> may issue it, and the
    /// letter must be legal under the match's <c>BanMode</c> (the picker UI only offers legal
    /// letters; the engine re-validates defensively).
    /// </summary>
    /// <param name="ActorUserId">The id of the player choosing the letter.</param>
    /// <param name="Letter">The chosen banned letter (case-insensitive; normalized lower-case).</param>
    public record SelectSniperBanCommand(Guid ActorUserId, char Letter)
        : AlphaChainCommand(ActorUserId);
}
