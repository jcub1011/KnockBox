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

        public DndMapperGameState(User host, ILogger<DndMapperGameState> logger)
            : base(host, logger)
        {
        }

        internal void SetPhase(DndMapperPhase phase) => Phase = phase;
        internal void SetSettings(DndMapperSettings settings) => Settings = settings;
        internal void SetAttributeSchema(AttributeSchema schema) => AttributeSchema = schema;
        internal void SetActiveMapId(Guid? mapId) => ActiveMapId = mapId;

        internal void AppendRoll(RollResult result)
        {
            RollLog.Add(result);
            if (RollLog.Count > RollLogCap)
                RollLog.RemoveRange(0, RollLog.Count - RollLogCap);
        }
    }
}
