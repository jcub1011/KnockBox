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
}
