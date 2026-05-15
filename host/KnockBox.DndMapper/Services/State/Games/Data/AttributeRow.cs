using KnockBox.DndMapper.Models;

namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed record AttributeRow(string Name, AttributeValueType Type, AttributeValue Default);
}
