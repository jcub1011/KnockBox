using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Services.State.Games
{
    public sealed class DndMapperGameState : AbstractGameState
    {
        public const int RollLogCap = 200;

        public DndMapperPhase Phase { get; private set; } = DndMapperPhase.Lobby;
        public DndMapperSettings Settings { get; private set; } = new();
        public AttributeSchema AttributeSchema { get; private set; }
            = AttributeSchema.FromPreset(AttributePreset.DnD5eCore);

        public List<Map> Maps { get; } = [];
        public Guid? ActiveMapId { get; private set; }

        public Dictionary<Guid, CharacterSheet> Sheets { get; } = [];
        public List<RollResult> RollLog { get; } = [];

        // Used as the per-room storage prefix for uploaded images and as the cleanup key
        // when the state is disposed. New per state; never reused across sessions.
        public Guid SessionId { get; } = Guid.NewGuid();

        // Running total of bytes consumed by uploaded images on this state. Used to
        // enforce the per-room 10 MB cap. Mutated only by image verbs inside Execute.
        public long BytesUsed { get; private set; }

        public DndMapperGameState(User host, ILogger<DndMapperGameState> logger)
            : base(host, logger)
        {
        }

        internal void SetPhase(DndMapperPhase phase) => Phase = phase;
        internal void SetSettings(DndMapperSettings settings) => Settings = settings;
        internal void SetAttributeSchema(AttributeSchema schema) => AttributeSchema = schema;
        internal void SetActiveMapId(Guid? mapId) => ActiveMapId = mapId;
        internal void SetBytesUsed(long value) => BytesUsed = value < 0 ? 0 : value;
        internal void AdjustBytesUsed(long delta) => BytesUsed = Math.Max(0, BytesUsed + delta);

        internal void AppendRoll(RollResult result)
        {
            RollLog.Add(result);
            if (RollLog.Count > RollLogCap)
                RollLog.RemoveRange(0, RollLog.Count - RollLogCap);
        }
    }
}
