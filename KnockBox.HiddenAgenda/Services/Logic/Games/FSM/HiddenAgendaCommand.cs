using System.Collections.Generic;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.FSM
{
    public abstract record HiddenAgendaCommand(string PlayerId);

    // Event Card Phase
    public record PlayCatalogCommand(string PlayerId, string TargetPlayerId) : HiddenAgendaCommand(PlayerId);
    public record PlayDetourCommand(string PlayerId, string TargetPlayerId) : HiddenAgendaCommand(PlayerId);
    public record SkipEventCardCommand(string PlayerId) : HiddenAgendaCommand(PlayerId);

    // Spin Phase
    public record SpinCommand(string PlayerId) : HiddenAgendaCommand(PlayerId);

    // Move Phase
    public record SelectDestinationCommand(string PlayerId, int DestinationSpaceId) : HiddenAgendaCommand(PlayerId);

    // Draw Phase
    public record SelectCurationCardCommand(string PlayerId, int CardIndex) : HiddenAgendaCommand(PlayerId);
    public record SelectTradeOptionCommand(string PlayerId, bool UseAlternate) : HiddenAgendaCommand(PlayerId);
    public record SelectEventCardActionCommand(string PlayerId, bool KeepNewCard) : HiddenAgendaCommand(PlayerId);

    // Guess Phase
    public record SubmitGuessCommand(string PlayerId, Dictionary<string, List<string>> Guesses) : HiddenAgendaCommand(PlayerId);
    public record SkipGuessCommand(string PlayerId) : HiddenAgendaCommand(PlayerId);

    // Final Guess Phase
    public record SubmitFinalGuessCommand(string PlayerId, Dictionary<string, List<string>> Guesses) : HiddenAgendaCommand(PlayerId);
    public record SkipFinalGuessCommand(string PlayerId) : HiddenAgendaCommand(PlayerId);

    // Round Over
    public record StartNextRoundCommand(string PlayerId) : HiddenAgendaCommand(PlayerId);

    // Match Over
    public record ReturnToLobbyCommand(string PlayerId) : HiddenAgendaCommand(PlayerId);
    public record PlayAgainCommand(string PlayerId) : HiddenAgendaCommand(PlayerId);
}
