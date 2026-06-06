namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed record CombatantEntry
    {
        public Guid Id { get; init; }
        public Guid TokenId { get; init; }
        public string Name { get; init; } = string.Empty;
        public Guid? OwnerUserId { get; init; }
        public int? InitiativeRoll { get; init; }
        public bool IsForceRolled { get; init; }

        // Host-typed NPC initiative that hasn't fired its dice yet. Set by
        // SetNpcInitiativeAsync; held until either (a) every NPC has a value
        // (pending or final) — at which point the engine commits all pending
        // values as one batch of RollResults — or (b) the host triggers
        // Roll All Unset NPCs, which also flushes pending values. Decouples
        // the host's data entry from the dice reveal so manual sets don't
        // pop the result instantly and ruin the "did it land on 17?" beat.
        public int? PendingInitiative { get; init; }
    }
}
