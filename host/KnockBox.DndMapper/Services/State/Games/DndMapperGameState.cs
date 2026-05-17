using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Services.State.Games
{
    public sealed class DndMapperGameState : AbstractGameState
    {
        public const int RollLogCap = 50;

        public DndMapperPhase Phase { get; private set; } = DndMapperPhase.Lobby;
        public DndMapperSettings Settings { get; private set; } = new();
        public AttributeSchema AttributeSchema { get; private set; }
            = AttributeSchema.FromPreset(AttributePreset.DnD5eCore);

        public List<Map> Maps { get; } = [];
        public Guid? ActiveMapId { get; private set; }

        public Dictionary<Guid, CharacterSheet> Sheets { get; } = [];
        public Dictionary<Guid, NamedTemplate> CustomTemplates { get; } = [];
        public List<RollResult> RollLog { get; } = [];

        // Running total of bytes consumed by uploaded images on this state. Used to
        // enforce the per-room 10 MB cap. Mutated only by image verbs inside Execute.
        public long BytesUsed { get; private set; }

        // Deterministic IDs so library snapshots that reference built-ins by
        // name don't accidentally collide with user-saved templates.
        public static readonly Guid BuiltInDnD5eCoreId = new("d0000000-0000-0000-0000-000000000001");
        public static readonly Guid BuiltInDnD5ePlusSkillsId = new("d0000000-0000-0000-0000-000000000002");
        public static readonly Guid BuiltInSimpleD20Id = new("d0000000-0000-0000-0000-000000000003");

        public DndMapperGameState(User host, ILogger<DndMapperGameState> logger)
            : base(host, logger)
        {
            SeedBuiltInTemplates();
        }

        internal void SeedBuiltInTemplates()
        {
            AddBuiltIn(BuiltInDnD5eCoreId, "D&D 5e core", AttributePreset.DnD5eCore);
            AddBuiltIn(BuiltInDnD5ePlusSkillsId, "D&D 5e + skills", AttributePreset.DnD5ePlusCommonSkills);
            AddBuiltIn(BuiltInSimpleD20Id, "Simple d20", AttributePreset.SimpleD20);

            void AddBuiltIn(Guid id, string name, AttributePreset preset)
            {
                CustomTemplates[id] = new NamedTemplate
                {
                    Id = id,
                    Name = name,
                    Rows = [.. AttributeSchema.FromPreset(preset).Rows],
                    IsBuiltIn = true,
                };
            }
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
