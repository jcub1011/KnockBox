using KnockBox.DndMapper.Models;

namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed record Token
    {
        public Guid Id { get; init; }
        public TokenType Type { get; init; }
        public Guid? OwnerUserId { get; init; }
        public Guid? RepresentsUserId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Color { get; init; } = string.Empty;
        public TokenIconKind IconKind { get; init; } = TokenIconKind.Initial;
        public Guid MapId { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public Guid? SheetId { get; init; }
        public bool Hidden { get; init; }
    }
}
