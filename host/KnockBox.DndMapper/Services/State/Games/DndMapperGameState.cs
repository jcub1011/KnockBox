using System.Collections.Immutable;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapper.Services.State.Games.Data.LoadedDice;

namespace KnockBox.DndMapper.Services.State.Games
{
    public sealed class DndMapperGameState : AbstractGameState
    {
        public const int RollLogCap = 50;

        public DndMapperPhase Phase { get; private set; } = DndMapperPhase.Lobby;
        public DndMapperSettings Settings { get; private set; } = new();
        public AttributeSchema AttributeSchema { get; private set; }
            = AttributeSchema.FromPreset(AttributePreset.DnD5eCore);

        public ImmutableArray<Map> Maps { get; internal set; } = ImmutableArray<Map>.Empty;
        public Guid? ActiveMapId { get; private set; }

        public ImmutableDictionary<Guid, CharacterSheet> Sheets { get; internal set; }
            = ImmutableDictionary<Guid, CharacterSheet>.Empty;
        public ImmutableDictionary<Guid, NamedTemplate> CustomTemplates { get; internal set; }
            = ImmutableDictionary<Guid, NamedTemplate>.Empty;
        public ImmutableArray<RollResult> RollLog { get; internal set; } = ImmutableArray<RollResult>.Empty;

        // Host-managed roll templates that ride with the save slot and are
        // visible to every sheet. Players can't author or edit these.
        public ImmutableArray<RollTemplate> GlobalRollTemplates { get; internal set; }
            = ImmutableArray<RollTemplate>.Empty;

        // Status-effect templates live as children of NamedTemplate
        // (attribute schemas). This id pins which schema the Effect Library
        // modal and the quick-apply dropdown read from. Null when the active
        // schema is a free-form Custom one not yet saved as a named template.
        public Guid? ActiveSchemaTemplateId { get; private set; }

        // Runtime source-of-truth for the attribute the engine adds to
        // initiative rolls. Lives on state (not just on NamedTemplate) so the
        // host can pick one even on a free-form Custom schema, and so the
        // choice survives reload even when no named template is active.
        // NamedTemplate.InitiativeAttributeName still carries each template's
        // preferred attribute; applying a template syncs this field from it.
        public string? InitiativeAttributeName { get; private set; }

        public CombatState? ActiveCombat { get; private set; }
        public CenterViewportRequest? PendingCenterRequest { get; private set; }

        // Host-drawn focus rectangle that drives the display view's SVG viewBox.
        // Lives on state (so the display circuit can observe it), but is not
        // serialized — a process restart clears it.
        public FocusRect? FocusRect { get; private set; }

        // Running total of bytes consumed by uploaded images on this state. Used to
        // enforce the per-room 1 GB cap. Mutated only by image verbs inside Execute.
        public long BytesUsed { get; private set; }

        // Host-authored loaded-dice rules. Persisted with the library
        // snapshot when Settings.LoadedDiceEnabled has ever been true; safe
        // to keep populated even while the master toggle is off — the engine
        // checks the toggle before evaluating.
        public ImmutableArray<LoadedDiceRule> LoadedDiceRules { get; internal set; }
            = ImmutableArray<LoadedDiceRule>.Empty;

        // Live snapshot of keys the host's client reports as currently held.
        // Ephemeral — process restarts clear it. Consumed only by
        // HostKeyHeldCondition; updated via UpdateHostInputStateAsync.
        public ImmutableHashSet<string> HostHeldKeys { get; internal set; }
            = ImmutableHashSet<string>.Empty;

        // Deterministic IDs so library snapshots that reference built-ins by
        // name don't accidentally collide with user-saved templates.
        public static readonly Guid BuiltInDnD5eCoreId = new("d0000000-0000-0000-0000-000000000001");
        public static readonly Guid BuiltInDnD5ePlusSkillsId = new("d0000000-0000-0000-0000-000000000002");
        public static readonly Guid BuiltInSimpleD20Id = new("d0000000-0000-0000-0000-000000000003");

        // Built-in roll templates — bare dice with no attribute mod or flat,
        // never serialized, never edited. The set is intentionally small;
        // anything more elaborate (Initiative +DEX, attack rolls, etc.)
        // belongs as a host-authored global or a per-sheet template.
        public static readonly Guid BuiltInRollD4Id = new("d0000000-0000-0000-0000-000000000101");
        public static readonly Guid BuiltInRollD6Id = new("d0000000-0000-0000-0000-000000000102");
        public static readonly Guid BuiltInRollD8Id = new("d0000000-0000-0000-0000-000000000103");
        public static readonly Guid BuiltInRollD10Id = new("d0000000-0000-0000-0000-000000000104");
        public static readonly Guid BuiltInRollD12Id = new("d0000000-0000-0000-0000-000000000105");
        public static readonly Guid BuiltInRollD20Id = new("d0000000-0000-0000-0000-000000000106");
        public static readonly Guid BuiltInRollD100Id = new("d0000000-0000-0000-0000-000000000107");
        public static readonly Guid BuiltInRoll2d6Id = new("d0000000-0000-0000-0000-000000000108");
        public static readonly Guid BuiltInRoll4d6Id = new("d0000000-0000-0000-0000-000000000109");

        public static readonly IReadOnlyList<RollTemplate> BuiltInRollTemplates =
        [
            BuiltInRoll(BuiltInRollD4Id, "d4", 1, 4),
            BuiltInRoll(BuiltInRollD6Id, "d6", 1, 6),
            BuiltInRoll(BuiltInRollD8Id, "d8", 1, 8),
            BuiltInRoll(BuiltInRollD10Id, "d10", 1, 10),
            BuiltInRoll(BuiltInRollD12Id, "d12", 1, 12),
            BuiltInRoll(BuiltInRollD20Id, "d20", 1, 20),
            BuiltInRoll(BuiltInRollD100Id, "d100", 1, 100),
            BuiltInRoll(BuiltInRoll2d6Id, "2d6", 2, 6),
            BuiltInRoll(BuiltInRoll4d6Id, "4d6", 4, 6),
        ];

        private static RollTemplate BuiltInRoll(Guid id, string name, int count, int sides) =>
            new(id, name, [new DiceTerm(count, sides)], 0, RollMode.Normal, null, name, RollTemplateScope.BuiltIn);

        public static bool IsBuiltInRollTemplateId(Guid id)
        {
            foreach (var t in BuiltInRollTemplates)
                if (t.Id == id) return true;
            return false;
        }

        public DndMapperGameState(User host, ILogger<DndMapperGameState> logger)
            : base(host, logger)
        {
            SeedBuiltInTemplates();
            ActiveSchemaTemplateId = BuiltInDnD5eCoreId;
            InitiativeAttributeName = CustomTemplates[BuiltInDnD5eCoreId].InitiativeAttributeName;
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
            // Each built-in pins its own initiative attribute so combat works
            // without the host having to configure one. User-saved Custom
            // schemas still start unset and fall back to the legacy
            // case-insensitive DEX lookup until the host picks one.
            var builder = CustomTemplates.ToBuilder();
            AddBuiltIn(builder, BuiltInDnD5eCoreId, "D&D 5e core", AttributePreset.DnD5eCore, initiativeAttribute: "DEX");
            AddBuiltIn(builder, BuiltInDnD5ePlusSkillsId, "D&D 5e + skills", AttributePreset.DnD5ePlusCommonSkills, initiativeAttribute: "DEX");
            AddBuiltIn(builder, BuiltInSimpleD20Id, "Simple d20", AttributePreset.SimpleD20, initiativeAttribute: "Modifier");
            CustomTemplates = builder.ToImmutable();

            static void AddBuiltIn(
                ImmutableDictionary<Guid, NamedTemplate>.Builder b,
                Guid id, string name, AttributePreset preset, string initiativeAttribute)
            {
                var rows = AttributeSchema.FromPreset(preset).Rows;
                b[id] = new NamedTemplate
                {
                    Id = id,
                    Name = name,
                    Rows = [.. rows],
                    IsBuiltIn = true,
                    InitiativeAttributeName = initiativeAttribute,
                };
            }
        }

        internal void SetPhase(DndMapperPhase phase) => Phase = phase;
        internal void SetSettings(DndMapperSettings settings) => Settings = settings;
        internal void SetAttributeSchema(AttributeSchema schema) => AttributeSchema = schema;
        internal void SetActiveSchemaTemplateId(Guid? id) => ActiveSchemaTemplateId = id;
        internal void SetInitiativeAttributeName(string? name) => InitiativeAttributeName = string.IsNullOrEmpty(name) ? null : name;
        internal void SetActiveMapId(Guid? mapId) => ActiveMapId = mapId;
        internal void SetActiveCombat(CombatState? combat) => ActiveCombat = combat;
        internal void SetPendingCenterRequest(CenterViewportRequest? request) => PendingCenterRequest = request;
        internal void SetFocusRect(FocusRect? rect) => FocusRect = rect;
        internal void SetBytesUsed(long value) => BytesUsed = value < 0 ? 0 : value;
        internal void AdjustBytesUsed(long delta) => BytesUsed = Math.Max(0, BytesUsed + delta);
        internal void SetLoadedDiceRules(ImmutableArray<LoadedDiceRule> rules) => LoadedDiceRules = rules;
        internal void SetHostHeldKeys(ImmutableHashSet<string> keys) => HostHeldKeys = keys;

        internal void AppendRoll(RollResult result)
        {
            var next = RollLog.Add(result);
            if (next.Length > RollLogCap)
                next = next.RemoveRange(0, next.Length - RollLogCap);
            RollLog = next;
        }

        // ── Update helpers ────────────────────────────────────────────────────
        //
        // Each helper finds the entity by id and replaces it in the appropriate
        // immutable collection via the update lambda. Callers express mutations
        // as `state.UpdateToken(mapId, tokenId, t => t with { X = newX })`.
        // Must be called from inside state.Execute.

        // Returns true when the map exists and the update lambda was applied;
        // false when the map id is unknown. Mirrors the existing engine pattern
        // of guarding with FirstOrDefault checks.
        internal bool UpdateMap(Guid mapId, Func<Map, Map> update)
        {
            var idx = IndexOfMap(mapId);
            if (idx < 0) return false;
            var current = Maps[idx];
            var next = update(current);
            if (!ReferenceEquals(next, current))
                Maps = Maps.SetItem(idx, next);
            return true;
        }

        internal bool UpdateToken(Guid mapId, Guid tokenId, Func<Token, Token> update)
        {
            var mapIdx = IndexOfMap(mapId);
            if (mapIdx < 0) return false;
            var map = Maps[mapIdx];
            var tokenIdx = IndexOfToken(map, tokenId);
            if (tokenIdx < 0) return false;
            var current = map.Tokens[tokenIdx];
            var next = update(current);
            if (ReferenceEquals(next, current)) return true;
            var nextMap = map with { Tokens = map.Tokens.SetItem(tokenIdx, next) };
            Maps = Maps.SetItem(mapIdx, nextMap);
            return true;
        }

        internal bool UpdateSheet(Guid sheetId, Func<CharacterSheet, CharacterSheet> update)
        {
            if (!Sheets.TryGetValue(sheetId, out var current)) return false;
            var next = update(current);
            if (!ReferenceEquals(next, current))
                Sheets = Sheets.SetItem(sheetId, next);
            return true;
        }

        // Linear scans; map/sheet counts are small and the existing engine
        // code uses FirstOrDefault on the same collections, so we match the
        // existing scaling rather than over-engineering with hash indexes.
        internal int IndexOfMap(Guid mapId)
        {
            for (var i = 0; i < Maps.Length; i++)
                if (Maps[i].Id == mapId) return i;
            return -1;
        }

        internal static int IndexOfToken(Map map, Guid tokenId)
        {
            for (var i = 0; i < map.Tokens.Length; i++)
                if (map.Tokens[i].Id == tokenId) return i;
            return -1;
        }

        internal static int IndexOfImage(Map map, Guid imageId)
        {
            for (var i = 0; i < map.Images.Length; i++)
                if (map.Images[i].Id == imageId) return i;
            return -1;
        }
    }
}
