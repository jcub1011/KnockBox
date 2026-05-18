using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Services.Logic.Visibility
{
    /// <summary>
    /// Pure visibility / edit-permission rules for character sheets.
    /// Host is always exempt — <c>viewerIsHost == true</c> short-circuits to
    /// allowed regardless of the configured <see cref="SheetEditPolicy"/>.
    /// Note that <see cref="SheetEditPolicy.HostOnly"/> applies to edits only:
    /// it means only the host may edit (sheet owners included are blocked),
    /// not that only the host may see the sheet — visibility is governed
    /// separately by <see cref="CanSeeSheet"/> and the per-game
    /// <c>PlayersCanSeeOtherSheets</c> setting.
    /// </summary>
    public static class SheetVisibilityHelper
    {
        public static bool CanSeeNotesAndHp(CharacterSheet sheet, string viewerUserId, bool viewerIsHost)
        {
            ArgumentNullException.ThrowIfNull(sheet);
            return viewerIsHost || sheet.OwnerUserId == viewerUserId;
        }

        /// <summary>
        /// Whether the viewer should see this sheet at all (e.g. in a tab list).
        /// Host sees every sheet. Players always see their own sheet; whether they
        /// see *other* players' sheets depends on the per-game
        /// <c>PlayersCanSeeOtherSheets</c> setting. Host-owned NPC sheets
        /// (null owner) are never visible to players.
        /// </summary>
        public static bool CanSeeSheet(
            CharacterSheet sheet,
            string viewerUserId,
            bool viewerIsHost,
            bool playersCanSeeOtherSheets)
        {
            ArgumentNullException.ThrowIfNull(sheet);
            if (viewerIsHost) return true;
            if (sheet.OwnerUserId is null) return false;
            if (sheet.OwnerUserId == viewerUserId) return true;
            return playersCanSeeOtherSheets;
        }

        public static bool CanEdit(
            CharacterSheet sheet,
            string viewerUserId,
            bool viewerIsHost,
            SheetEditPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(sheet);
            if (viewerIsHost) return true;
            return policy switch
            {
                SheetEditPolicy.HostOnly => false,
                SheetEditPolicy.OwnersAndHost => sheet.OwnerUserId == viewerUserId,
                SheetEditPolicy.Anyone => true,
                _ => false,
            };
        }
    }
}
