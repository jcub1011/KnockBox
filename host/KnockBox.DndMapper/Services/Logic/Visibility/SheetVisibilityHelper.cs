using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Services.Logic.Visibility
{
    /// <summary>
    /// Pure visibility / edit-permission rules for character sheets.
    /// Host is always exempt — when <c>viewerIsHost</c> is true, both
    /// <see cref="SheetEditPolicy.OwnersOnly"/> and
    /// <see cref="SheetEditPolicy.OwnersAndHost"/> short-circuit to the
    /// same outcome (allowed). Asserted in unit tests.
    /// </summary>
    public static class SheetVisibilityHelper
    {
        public static bool CanSeeNotesAndHp(CharacterSheet sheet, string viewerUserId, bool viewerIsHost)
        {
            ArgumentNullException.ThrowIfNull(sheet);
            return viewerIsHost || sheet.OwnerUserId == viewerUserId;
        }

        public static bool CanEdit(
            CharacterSheet sheet,
            string viewerUserId,
            bool viewerIsHost,
            SheetEditPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(sheet);
            if (viewerIsHost) return true;
            if (sheet.OwnerUserId == viewerUserId) return true;
            return policy == SheetEditPolicy.Anyone;
        }
    }
}
