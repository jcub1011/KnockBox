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

        // Status-effect templates live as children of NamedTemplate
        // (attribute schemas). This id pins which schema the Effect Library
        // modal and the quick-apply dropdown read from. Null when the active
        // schema is a free-form Custom one not yet saved as a named template.
        public Guid? ActiveSchemaTemplateId { get; private set; }

        public CombatState? ActiveCombat { get; private set; }
        public CenterViewportRequest? PendingCenterRequest { get; private set; }

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
            ActiveSchemaTemplateId = BuiltInDnD5eCoreId;
        }

        // Maps a preset onto its deterministic built-in template id so the
        // Effect Library can find the active schema's effects without the
        // caller having to track the id explicitly.
        public static Guid? BuiltInTemplateIdFor(AttributePreset preset) => preset switch
        {
            AttributePreset.DnD5eCore => BuiltInDnD5eCoreId,
            AttributePreset.DnD5ePlusCommonSkills => BuiltInDnD5ePlusSkillsId,
            AttributePreset.SimpleD20 => BuiltInSimpleD20Id,
            _ => null,
        };

        public NamedTemplate? GetActiveSchemaTemplate()
            => ActiveSchemaTemplateId is { } id && CustomTemplates.TryGetValue(id, out var t)
                ? t
                : null;

        // Players who joined the lobby before the game started may rejoin
        // mid-session (e.g. after a circuit drop past the reconnect grace
        // window). Strangers and kicked players are still rejected by the
        // base gates.
        protected override bool AllowRejoinAfterStart => true;

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
        internal void SetActiveSchemaTemplateId(Guid? id) => ActiveSchemaTemplateId = id;
        internal void SetActiveMapId(Guid? mapId) => ActiveMapId = mapId;
        internal void SetActiveCombat(CombatState? combat) => ActiveCombat = combat;
        internal void SetPendingCenterRequest(CenterViewportRequest? request) => PendingCenterRequest = request;
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
