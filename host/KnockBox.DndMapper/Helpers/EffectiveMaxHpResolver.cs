using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Helpers
{
    /// <summary>
    /// Resolves a sheet's effective MaxHp by summing the base MaxHp with every
    /// active StatusEffect.MaxHpDelta. Returns null when the sheet's MaxHp is
    /// itself null (HP tracking disabled).
    /// </summary>
    public static class EffectiveMaxHpResolver
    {
        public static int? ResolveEffectiveMaxHp(CharacterSheet sheet)
        {
            if (sheet?.MaxHp is not int baseMax) return null;
            int delta = 0;
            foreach (var effect in sheet.StatusEffects)
                delta += effect.MaxHpDelta ?? 0;
            return baseMax + delta;
        }
    }
}
