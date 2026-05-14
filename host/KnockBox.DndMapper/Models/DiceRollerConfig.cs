using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Models
{
    // Mutable per-circuit dice roller config, shared between DiceRollerModal and
    // the roll-log quick-roll button so closing the modal doesn't lose the last
    // configured dice setup.
    public sealed class DiceRollerConfig
    {
        public List<DiceTerm> Terms { get; set; } = [new DiceTerm(1, 20)];
        public Guid? PickerSheetId { get; set; }
        public string? AttributeName { get; set; }
        public int FlatModifier { get; set; }
        public RollMode Mode { get; set; } = RollMode.Normal;
        public string Label { get; set; } = string.Empty;
    }
}
