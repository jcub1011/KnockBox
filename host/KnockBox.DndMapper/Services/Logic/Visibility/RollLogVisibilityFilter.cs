using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Services.Logic.Visibility
{
    public static class RollLogVisibilityFilter
    {
        public static IEnumerable<RollResult> VisibleTo(
            IEnumerable<RollResult> log,
            string viewerUserId,
            bool viewerIsHost,
            bool rollsVisibleToPlayers)
        {
            ArgumentNullException.ThrowIfNull(log);
            if (viewerIsHost) return log;
            if (rollsVisibleToPlayers) return log;
            return log.Where(r => r.RollerUserId == viewerUserId);
        }
    }
}
