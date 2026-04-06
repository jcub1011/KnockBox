using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace KnockBox.Services.Navigation.Games
{
    public enum GameType
    {
        [Description("Split The Deck")]
        [NavigationString("split-the-deck")]
        SplitTheDeck,
        [Description("Dice Simulator")]
        [NavigationString("dice-simulator")]
        DiceSimulator,
        [Description("Card Counter")]
        [NavigationString("card-counter")]
        CardCounter,
        [Description("Drawn To Dress")]
        [NavigationString("drawn-to-dress")]
        DrawnToDress,
        [Description("Consult The Card")]
        [NavigationString("consult-the-card")]
        ConsultTheCard,
        [Description("Operator")]
        [NavigationString("operator")]
        Operator,
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public class NavigationString(string uri) : Attribute
    {
        public readonly string Uri = uri;

        public static string? GetNavigationStringAttribute(Enum value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = field?.GetCustomAttribute<NavigationString>();
            return attribute?.Uri;
        }
    }

    public static class NavigationStringExtensions 
    {
        public static bool TryGetNavigationString(this GameType gameType, [NotNullWhen(true)] out string? navigationString)
        {
            navigationString = NavigationString.GetNavigationStringAttribute(gameType);
            return navigationString is not null;
        }
    }

    public static class DescriptionExtensions 
    {
        public static bool TryGetDescription(this Enum value, [NotNullWhen(true)] out string? description)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = field?.GetCustomAttribute<DescriptionAttribute>();
            description = attribute?.Description;
            return description is not null;
        }
    }
}
