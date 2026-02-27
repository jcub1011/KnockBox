using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace KnockBox.Services.Navigation.Games
{
    public enum GameType
    {
        [NavigationString("split-the-deck")]
        SplitTheDeck
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
}
