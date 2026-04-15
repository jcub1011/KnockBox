namespace KnockBox.HiddenAgenda.Services.Logic.Games.Data;

public enum EventCardType { Catalog, Detour }

public record EventCard(EventCardType Type, string Description);

public static class EventCardDefinitions
{
    public static readonly EventCard Catalog = new(EventCardType.Catalog, "View another player's last 3 drawn Curation Cards (including what they discarded). The target knows they were Cataloged but not what you learned.");
    public static readonly EventCard Detour = new(EventCardType.Detour, "After spinning, use another player's last movement (their previous spinner result and destination) instead of your own.");
}
