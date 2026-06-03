using System.Collections.Generic;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.FSM
{
    public abstract record HiddenAgendaCommand(Guid PlayerId);

    // Event Card Phase
    public record PlayCatalogCommand(Guid PlayerId, Guid TargetPlayerId) : HiddenAgendaCommand(PlayerId);
    public record PlayDetourCommand(Guid PlayerId, Guid TargetPlayerId) : HiddenAgendaCommand(PlayerId);
    public record SkipEventCardCommand(Guid PlayerId) : HiddenAgendaCommand(PlayerId);

    // Spin Phase
    public record SpinCommand(Guid PlayerId) : HiddenAgendaCommand(PlayerId);

    // Move Phase
    public record SelectDestinationCommand(Guid PlayerId, int DestinationSpaceId) : HiddenAgendaCommand(PlayerId);

    // Draw Phase
    public record SelectCurationCardCommand(Guid PlayerId, int CardIndex) : HiddenAgendaCommand(PlayerId);
    public record SelectTradeOptionCommand(Guid PlayerId, bool UseAlternate) : HiddenAgendaCommand(PlayerId);
    public record SelectEventCardActionCommand(Guid PlayerId, bool KeepNewCard) : HiddenAgendaCommand(PlayerId);

    // Call Vote (any player, during turn phases)
    public record CallVoteCommand(Guid PlayerId) : HiddenAgendaCommand(PlayerId);

    // Guess Phase
    public record SubmitGuessCommand(Guid PlayerId, Dictionary<Guid, List<string>> Guesses) : HiddenAgendaCommand(PlayerId);
    public record SkipGuessCommand(Guid PlayerId) : HiddenAgendaCommand(PlayerId);

    // Final Guess Phase
    public record SubmitFinalGuessCommand(Guid PlayerId, Dictionary<Guid, List<string>> Guesses) : HiddenAgendaCommand(PlayerId);
    public record SkipFinalGuessCommand(Guid PlayerId) : HiddenAgendaCommand(PlayerId);

    // Round Over
    public record StartNextRoundCommand(Guid PlayerId) : HiddenAgendaCommand(PlayerId);

    // Match Over
    public record ReturnToLobbyCommand(Guid PlayerId) : HiddenAgendaCommand(PlayerId);
    public record PlayAgainCommand(Guid PlayerId) : HiddenAgendaCommand(PlayerId);
}
