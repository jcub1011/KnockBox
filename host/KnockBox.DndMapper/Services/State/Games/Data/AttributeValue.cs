using KnockBox.DndMapper.Models;

namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed record AttributeValue
    {
        public AttributeValueType Type { get; }
        public int? IntValue { get; }
        public string? StringValue { get; }

        private AttributeValue(AttributeValueType type, int? intValue, string? stringValue)
        {
            Type = type;
            IntValue = intValue;
            StringValue = stringValue;
        }

        public static AttributeValue Score(int score) => new(AttributeValueType.Score, score, null);
        public static AttributeValue Modifier(int modifier) => new(AttributeValueType.Modifier, modifier, null);
        public static AttributeValue Text(string text) => new(AttributeValueType.Text, null, text ?? string.Empty);

        public int? GetModifier() => Type switch
        {
            AttributeValueType.Score when IntValue is int score => (int)Math.Floor((score - 10) / 2.0),
            AttributeValueType.Modifier => IntValue,
            _ => null,
        };
    }
}
