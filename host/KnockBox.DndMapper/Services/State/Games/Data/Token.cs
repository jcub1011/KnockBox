using KnockBox.DndMapper.Models;

namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed class Token
    {
        public Guid Id { get; set; }
        public TokenType Type { get; set; }
        public string? OwnerUserId { get; set; }
        public string? RepresentsUserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public TokenIconKind IconKind { get; set; } = TokenIconKind.Initial;
        public Guid MapId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public Guid? SheetId { get; set; }
        public bool Hidden { get; set; }
    }
}
