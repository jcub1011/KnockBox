namespace KnockBox.DndMapper.Models
{
    public enum TokenType
    {
        PlayerToken,
        // Everything that isn't a PlayerToken. A null OwnerUserId means host-owned
        // (the typical case). A non-null OwnerUserId means a player created/owns the
        // NPC (allowed only when DndMapperSettings.PlayersCanCreateNPCs is set).
        // An NPC may optionally carry RepresentsUserId — set when the host wants to
        // pilot a character that stands in for a specific player (DMPC) or when a
        // PlayerToken was auto-orphaned because its player left mid-session.
        NPCToken,
    }
}
