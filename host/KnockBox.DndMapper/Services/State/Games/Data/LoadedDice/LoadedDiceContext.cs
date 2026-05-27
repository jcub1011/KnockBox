using System.Collections.Immutable;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;

namespace KnockBox.DndMapper.Services.State.Games.Data.LoadedDice
{
    // Snapshot of everything strategies are allowed to inspect during rule
    // evaluation. Built once per RollAsync invocation so individual condition
    // and modification calls stay cheap and side-effect-free. RollNewDie is
    // the only "live" hook — used by Bias* modifications that need a fresh
    // RNG draw. In tests it's swapped for a deterministic stub.
    public sealed record LoadedDiceContext
    {
        public required User Caller { get; init; }
        public required DndMapperGameState State { get; init; }
        public required RollRequest Request { get; init; }
        // Sheet associated with the roll, if any. Null when the roller did
        // not attribute the roll to a sheet (raw d20 from the dice tray).
        public Guid? RollerSheetId { get; init; }
        // Sides of the die currently being modified. Strategies use this for
        // dice-type targeting and for clamping after Set/Bias modifications.
        public required int DiceTermSides { get; init; }
        // Currently-held keys (logical key names — "Space", "Shift", "A",
        // etc.) on the host's client. Updated by UpdateHostInputStateAsync
        // and ephemeral (not persisted across process restarts).
        public required ImmutableHashSet<string> HostHeldKeys { get; init; }
        // Fresh-die roll function. In production wraps IRng.GetRandomInt; in
        // tests can be replaced with a deterministic sequence so Bias*
        // modifications are reproducible.
        public required Func<int, int> RollNewDie { get; init; }
    }
}
