using KnockBox.DndMapper.Models;

namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed class AttributeSchema
    {
        public AttributePreset Preset { get; }
        public IReadOnlyList<AttributeRow> Rows { get; }

        public AttributeSchema(AttributePreset preset, IReadOnlyList<AttributeRow> rows)
        {
            Preset = preset;
            Rows = rows;
        }

        public static AttributeSchema FromPreset(AttributePreset preset) => preset switch
        {
            AttributePreset.DnD5eCore => new AttributeSchema(preset, BuildDnD5eCoreRows()),
            AttributePreset.DnD5ePlusCommonSkills => new AttributeSchema(preset, BuildDnD5ePlusCommonSkillsRows()),
            AttributePreset.SimpleD20 => new AttributeSchema(preset, BuildSimpleD20Rows()),
            AttributePreset.Custom => new AttributeSchema(preset, []),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown attribute preset."),
        };

        private static List<AttributeRow> BuildDnD5eCoreRows() =>
        [
            new AttributeRow("STR", AttributeValueType.Score, AttributeValue.Score(10)),
            new AttributeRow("DEX", AttributeValueType.Score, AttributeValue.Score(10)),
            new AttributeRow("CON", AttributeValueType.Score, AttributeValue.Score(10)),
            new AttributeRow("INT", AttributeValueType.Score, AttributeValue.Score(10)),
            new AttributeRow("WIS", AttributeValueType.Score, AttributeValue.Score(10)),
            new AttributeRow("CHA", AttributeValueType.Score, AttributeValue.Score(10)),
        ];

        private static List<AttributeRow> BuildDnD5ePlusCommonSkillsRows()
        {
            var rows = BuildDnD5eCoreRows();
            rows.Add(new AttributeRow("Athletics", AttributeValueType.Modifier, AttributeValue.Modifier(0)));
            rows.Add(new AttributeRow("Stealth", AttributeValueType.Modifier, AttributeValue.Modifier(0)));
            rows.Add(new AttributeRow("Perception", AttributeValueType.Modifier, AttributeValue.Modifier(0)));
            rows.Add(new AttributeRow("Persuasion", AttributeValueType.Modifier, AttributeValue.Modifier(0)));
            rows.Add(new AttributeRow("Investigation", AttributeValueType.Modifier, AttributeValue.Modifier(0)));
            return rows;
        }

        private static List<AttributeRow> BuildSimpleD20Rows() =>
        [
            new AttributeRow("Modifier", AttributeValueType.Modifier, AttributeValue.Modifier(0)),
        ];
    }
}
