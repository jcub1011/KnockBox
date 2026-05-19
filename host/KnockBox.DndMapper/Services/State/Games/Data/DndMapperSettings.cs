using KnockBox.DndMapper.Models;

namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed class DndMapperSettings
    {
        public TokenMovementPolicy TokenMovement { get; set; } = TokenMovementPolicy.OwnerOrHost;
        public SheetEditPolicy SheetEditByOthers { get; set; } = SheetEditPolicy.OwnersAndHost;
        public bool RollsVisibleToPlayers { get; set; } = true;
        public bool PlayersCanCreateNPCs { get; set; } = false;
        public bool HpTrackingEnabled { get; set; } = true;
        public bool PlayersCanSeeOtherSheets { get; set; } = false;

        public DndMapperSettings Clone() => new()
        {
            TokenMovement = TokenMovement,
            SheetEditByOthers = SheetEditByOthers,
            RollsVisibleToPlayers = RollsVisibleToPlayers,
            PlayersCanCreateNPCs = PlayersCanCreateNPCs,
            HpTrackingEnabled = HpTrackingEnabled,
            PlayersCanSeeOtherSheets = PlayersCanSeeOtherSheets,
        };
    }
}
