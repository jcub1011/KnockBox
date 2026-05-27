namespace KnockBox.DndMapper.Services.State.Games.Data.LoadedDice
{
    // Audit record stamped onto a RollResult when a rule fired. Carries the
    // rule's display name so the UI doesn't have to re-resolve the rule by id
    // (the rule may have been deleted or renamed by the time the log entry is
    // rendered, but the historical roll should still report what fired).
    public sealed record LoadedDiceRuleStamp(Guid RuleId, string RuleName);
}
