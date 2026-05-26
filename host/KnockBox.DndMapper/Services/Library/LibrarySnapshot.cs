using System.Collections.Immutable;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapper.Services.State.Games.Data.LoadedDice;

namespace KnockBox.DndMapper.Services.Library
{
    /// <summary>
    /// On-disk representation of the host's persistent DnD Mapper library.
    /// Decoupled from the runtime state classes so persisted shape evolves
    /// independently of in-memory data. <see cref="SchemaVersion"/> drives the
    /// IndexedDB upgrade callback; bump it whenever the contract here changes.
    /// </summary>
    /// <remarks>
    /// All persisted token entries hydrate as <see cref="TokenType.NPCToken"/>
    /// with <see cref="TokenSnapshot.OwnerUserId"/> nulled out, and all sheets
    /// get <see cref="SheetSnapshot.OwnerUserId"/> nulled, because
    /// session-scoped user ids change every session (sessionStorage). The host
    /// reassigns ownership via <c>ReassignTokenOwnerAsync</c> at hydration time.
    /// <para>
    /// Snapshot DTOs use plain init-only properties (no private constructors,
    /// no smart factories) so System.Text.Json can roundtrip them without
    /// custom converters.
    /// </para>
    /// </remarks>
    internal sealed record LibrarySnapshot
    {
        public int SchemaVersion { get; init; } = 3;
        public DndMapperSettings Settings { get; init; } = new();
        public AttributeSchemaSnapshot AttributeSchema { get; init; } = new();
        // Set when the live schema came from a known NamedTemplate (built-in
        // or user-saved). Null for free-form Custom schemas. Drives which
        // NamedTemplate's effect library is "active" on load.
        public Guid? ActiveSchemaTemplateId { get; init; }
        // State-level initiative attribute. Null on older snapshots (V1–V3);
        // the load path then falls back to the active template's value (or
        // the legacy DEX heuristic) so the host's choice still round-trips.
        public string? InitiativeAttributeName { get; init; }
        public List<MapSnapshot> Maps { get; init; } = [];
        public List<SheetSnapshot> Sheets { get; init; } = [];
        public List<NamedTemplateSnapshot> CustomTemplates { get; init; } = [];
        // Host-managed roll templates that ride with the save slot. Built-ins
        // are never serialized; sheet-scoped templates ride on SheetSnapshot.
        public List<RollTemplateSnapshot> GlobalRollTemplates { get; init; } = [];
        // Host-authored loaded-dice rules. Polymorphic Condition/Modification
        // subtypes round-trip via System.Text.Json's $kind discriminator.
        // Older snapshots without this field deserialize to [] — no schema
        // bump needed.
        public List<LoadedDiceRule> LoadedDiceRules { get; init; } = [];
    }

    /// <summary>
    /// v4 (per-slot sharded) "spine" record. Replaces the single
    /// <see cref="LibrarySnapshot"/> blob at key <c>{slotId}</c> with one
    /// small record at <c>{slotId}:core</c> plus one record per map at
    /// <c>{slotId}:map:{mapId}</c> and per sheet at <c>{slotId}:sheet:{sheetId}</c>.
    /// Token moves on Map A then only rewrite Map A's shard + the core spine,
    /// instead of re-base64-ing every map's fog mask in one fat record.
    /// </summary>
    internal sealed record LibraryCoreSnapshot
    {
        public int SchemaVersion { get; init; } = 4;
        public DndMapperSettings Settings { get; init; } = new();
        public AttributeSchemaSnapshot AttributeSchema { get; init; } = new();
        public Guid? ActiveSchemaTemplateId { get; init; }
        public string? InitiativeAttributeName { get; init; }
        public List<NamedTemplateSnapshot> CustomTemplates { get; init; } = [];
        public List<RollTemplateSnapshot> GlobalRollTemplates { get; init; } = [];
        // See LibrarySnapshot.LoadedDiceRules.
        public List<LoadedDiceRule> LoadedDiceRules { get; init; } = [];
        // Ordered list of map ids. Mirrors Map.ListOrder so LoadSlotAsync can
        // fan out shard reads in display order without a second sort.
        public List<Guid> MapIds { get; init; } = [];
        // Ordered list of sheet ids (insertion order in state.Sheets).
        public List<Guid> SheetIds { get; init; } = [];
    }

    internal sealed record RollTemplateSnapshot
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public List<DiceTermSnapshot> Dice { get; init; } = [];
        public int FlatModifier { get; init; }
        public RollMode Mode { get; init; } = RollMode.Normal;
        public string? AttributeName { get; init; }
        public string Label { get; init; } = string.Empty;
        // Scope is implicit by location (LibrarySnapshot.GlobalRollTemplates →
        // Global; SheetSnapshot.RollTemplates → Sheet) so we don't store it.
    }

    internal sealed record DiceTermSnapshot
    {
        public int Count { get; init; }
        public int Sides { get; init; }
    }

    internal sealed record NamedTemplateSnapshot
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public bool IsBuiltIn { get; init; }
        public List<AttributeRowSnapshot> Rows { get; init; } = [];
        // Host-authored status-effect templates scoped to this schema.
        public List<StatusEffectTemplateSnapshot> StatusEffectTemplates { get; init; } = [];
        // Attribute name used as the initiative modifier under this schema.
        // Null means "fall back to legacy DEX search".
        public string? InitiativeAttributeName { get; init; }
    }

    internal sealed record StatusEffectTemplateSnapshot
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public List<AttributeDeltaSnapshot> AttributeDeltas { get; init; } = [];
        public int? MaxHpDelta { get; init; }
        public int? OnApplyHpDelta { get; init; }
        public string Notes { get; init; } = string.Empty;
    }

    internal sealed record StatusEffectSnapshot
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public List<AttributeDeltaSnapshot> AttributeDeltas { get; init; } = [];
        public int? MaxHpDelta { get; init; }
        public int? OnApplyHpDelta { get; init; }
        public string Notes { get; init; } = string.Empty;
        public DateTime AppliedUtc { get; init; }
    }

    internal sealed record AttributeDeltaSnapshot
    {
        public string AttributeName { get; init; } = string.Empty;
        public int Delta { get; init; }
    }

    internal sealed record MapSnapshot
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int ListOrder { get; init; }
        public DateTime CreatedUtc { get; init; }
        public GridConfig Grid { get; init; } = new();
        public double? DefaultSpawnX { get; init; }
        public double? DefaultSpawnY { get; init; }
        public List<MapImageSnapshot> Images { get; init; } = [];
        public List<TokenSnapshot> Tokens { get; init; } = [];
        // Per-cell fog mask, packed row-major (cy * Grid.WidthCells + cx).
        // System.Text.Json serializes byte[] as base64. Older snapshots without
        // this field deserialize to [], which Map.IsFogged treats as "all
        // revealed" — no schema bump required.
        public byte[] FogMask { get; init; } = [];
    }

    internal sealed record MapImageSnapshot
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public double OriginalWidth { get; init; }
        public double OriginalHeight { get; init; }
        public double Rotation { get; init; }
        public double Opacity { get; init; } = 1.0;
        public int LayerOrder { get; init; }
        public bool Locked { get; init; }
        public bool Hidden { get; init; }
        public long ByteSize { get; init; }
        // Downscale provenance. Missing on legacy (pre-2026-05) snapshots:
        // System.Text.Json fills bool with false and int with 0, which the
        // Layers panel treats as "not downscaled" — no schema bump needed.
        public bool WasDownscaled { get; init; }
        public int OriginalLongEdgePx { get; init; }
        public int DisplayLongEdgePx { get; init; }
        // Intentionally no ShareToken: the capability is per-circuit and is
        // recomputed on every Attach via PublishForSharingAsync.
    }

    internal sealed record TokenSnapshot
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Color { get; init; } = string.Empty;
        public TokenIconKind IconKind { get; init; } = TokenIconKind.Initial;
        public Guid MapId { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public Guid? SheetId { get; init; }
        public bool Hidden { get; init; }
        // OwnerUserId / RepresentsUserId not persisted: previous-session user ids
        // are gone (sessionStorage). On hydration tokens load as NPCToken with
        // no owner and the host reassigns via ReassignTokenOwnerAsync.
    }

    internal sealed record SheetSnapshot
    {
        public Guid Id { get; init; }
        public string CharacterName { get; init; } = string.Empty;
        public Dictionary<string, AttributeValueSnapshot> Values { get; init; } = [];
        public string Notes { get; init; } = string.Empty;
        public int? Hp { get; init; }
        public int? MaxHp { get; init; }
        // New 2026-05: nullable AC, persisted color, map scoping. Older
        // snapshots without these keys deserialize to null / empty / null
        // respectively — no schema bump needed because the runtime defaults
        // match (AC unset, color falls back to token color, sheet is global).
        public int? ArmorClass { get; init; }
        public string Color { get; init; } = string.Empty;
        public Guid? ScopedMapId { get; init; }
        public List<StatusEffectSnapshot> StatusEffects { get; init; } = [];
        public List<RollTemplateSnapshot> RollTemplates { get; init; } = [];
        // OwnerUserId not persisted (see TokenSnapshot rationale).
    }

    internal sealed record AttributeSchemaSnapshot
    {
        public AttributePreset Preset { get; init; } = AttributePreset.DnD5eCore;
        public List<AttributeRowSnapshot> Rows { get; init; } = [];
    }

    internal sealed record AttributeRowSnapshot
    {
        public string Name { get; init; } = string.Empty;
        public AttributeValueType Type { get; init; }
        public AttributeValueSnapshot Default { get; init; } = new();
    }

    internal sealed record AttributeValueSnapshot
    {
        public AttributeValueType Type { get; init; }
        public int? IntValue { get; init; }
        public string? StringValue { get; init; }
    }

    /// <summary>Pure mapping helpers; no I/O, no locking. Caller must hold the read lock when snapshotting state.</summary>
    internal static class LibrarySnapshotMapper
    {
        public static LibrarySnapshot FromState(KnockBox.DndMapper.Services.State.Games.DndMapperGameState state)
        {
            // Retained as the in-memory composition used by (a) the v3 read
            // path during migration and (b) the LoadSlotAsync hydration body
            // after fanning out shard reads. Saves themselves now go through
            // ToCoreSnapshot + ToMapSnapshot + ToSheetSnapshot.
            var core = ToCoreSnapshot(state);
            var maps = state.Maps
                .OrderBy(m => m.ListOrder)
                .Select(ToMapSnapshot)
                .ToList();
            var sheets = state.Sheets.Values
                .Select(ToSheetSnapshot)
                .ToList();

            return new LibrarySnapshot
            {
                SchemaVersion = 3,
                Settings = core.Settings,
                AttributeSchema = core.AttributeSchema,
                ActiveSchemaTemplateId = core.ActiveSchemaTemplateId,
                InitiativeAttributeName = core.InitiativeAttributeName,
                Maps = maps,
                Sheets = sheets,
                CustomTemplates = core.CustomTemplates,
                GlobalRollTemplates = core.GlobalRollTemplates,
                LoadedDiceRules = core.LoadedDiceRules,
            };
        }

        /// <summary>Builds the v4 core "spine" — small per-slot record without map / sheet payloads.</summary>
        public static LibraryCoreSnapshot ToCoreSnapshot(KnockBox.DndMapper.Services.State.Games.DndMapperGameState state)
        {
            // Persist every NamedTemplate (built-in or user). For built-ins we
            // still write Rows so older hosts can read it, but on load the
            // seeded Rows are authoritative — only the StatusEffectTemplates
            // ride along to round-trip the host's authoring under built-in
            // schemas.
            var templates = state.CustomTemplates.Values
                .Select(t => new NamedTemplateSnapshot
                {
                    Id = t.Id,
                    Name = t.Name,
                    IsBuiltIn = t.IsBuiltIn,
                    Rows = t.Rows.Select(r => new AttributeRowSnapshot
                    {
                        Name = r.Name,
                        Type = r.Type,
                        Default = ToValueSnapshot(r.Default),
                    }).ToList(),
                    StatusEffectTemplates = t.StatusEffectTemplates
                        .Select(ToStatusEffectTemplateSnapshot)
                        .ToList(),
                    InitiativeAttributeName = t.InitiativeAttributeName,
                })
                .ToList();

            return new LibraryCoreSnapshot
            {
                SchemaVersion = 4,
                Settings = state.Settings with { },
                AttributeSchema = ToSchemaSnapshot(state.AttributeSchema),
                ActiveSchemaTemplateId = state.ActiveSchemaTemplateId,
                InitiativeAttributeName = state.InitiativeAttributeName,
                CustomTemplates = templates,
                GlobalRollTemplates = state.GlobalRollTemplates
                    .Select(ToRollTemplateSnapshot)
                    .ToList(),
                LoadedDiceRules = state.LoadedDiceRules.ToList(),
                MapIds = state.Maps
                    .OrderBy(m => m.ListOrder)
                    .Select(m => m.Id)
                    .ToList(),
                SheetIds = state.Sheets.Keys.ToList(),
            };
        }

        /// <summary>Builds a single map shard (no I/O). Safe to call concurrently
        /// with engine writes because Map is immutable — the engine swaps whole
        /// Map records in state.Maps rather than mutating fields in place.</summary>
        public static MapSnapshot ToMapSnapshot(Map map) => new()
        {
            Id = map.Id,
            Name = map.Name,
            ListOrder = map.ListOrder,
            CreatedUtc = map.CreatedUtc,
            Grid = map.Grid,
            DefaultSpawnX = map.DefaultSpawnPosition?.X,
            DefaultSpawnY = map.DefaultSpawnPosition?.Y,
            Images = map.Images
                .OrderBy(i => i.LayerOrder)
                .Select(ToImageSnapshot)
                .ToList(),
            Tokens = map.Tokens
                .Select(ToTokenSnapshot)
                .ToList(),
            FogMask = map.FogMask.IsDefaultOrEmpty ? [] : map.FogMask.ToArray(),
        };

        public static RollTemplateSnapshot ToRollTemplateSnapshot(RollTemplate t) => new()
        {
            Id = t.Id,
            Name = t.Name,
            Dice = t.Dice.Select(d => new DiceTermSnapshot { Count = d.Count, Sides = d.Sides }).ToList(),
            FlatModifier = t.FlatModifier,
            Mode = t.Mode,
            AttributeName = t.AttributeName,
            Label = t.Label,
        };

        public static RollTemplate FromRollTemplateSnapshot(RollTemplateSnapshot s, RollTemplateScope scope) =>
            new(
                s.Id,
                s.Name,
                s.Dice.Select(d => new DiceTerm(d.Count, d.Sides)).ToList(),
                s.FlatModifier,
                s.Mode,
                s.AttributeName,
                s.Label,
                scope);

        public static StatusEffectTemplateSnapshot ToStatusEffectTemplateSnapshot(StatusEffectTemplate t) => new()
        {
            Id = t.Id,
            Name = t.Name,
            AttributeDeltas = t.AttributeDeltas.Select(d => new AttributeDeltaSnapshot
            {
                AttributeName = d.AttributeName,
                Delta = d.Delta,
            }).ToList(),
            MaxHpDelta = t.MaxHpDelta,
            OnApplyHpDelta = t.OnApplyHpDelta,
            Notes = t.Notes,
        };

        public static StatusEffectSnapshot ToStatusEffectSnapshot(StatusEffect e) => new()
        {
            Id = e.Id,
            Name = e.Name,
            AttributeDeltas = e.AttributeDeltas.Select(d => new AttributeDeltaSnapshot
            {
                AttributeName = d.AttributeName,
                Delta = d.Delta,
            }).ToList(),
            MaxHpDelta = e.MaxHpDelta,
            OnApplyHpDelta = e.OnApplyHpDelta,
            Notes = e.Notes,
            AppliedUtc = e.AppliedUtc,
        };

        public static StatusEffectTemplate FromStatusEffectTemplateSnapshot(StatusEffectTemplateSnapshot s) => new()
        {
            Id = s.Id,
            Name = s.Name,
            AttributeDeltas = s.AttributeDeltas
                .Select(d => new AttributeDelta(d.AttributeName, d.Delta))
                .ToImmutableList(),
            MaxHpDelta = s.MaxHpDelta,
            OnApplyHpDelta = s.OnApplyHpDelta,
            Notes = s.Notes,
        };

        public static StatusEffect FromStatusEffectSnapshot(StatusEffectSnapshot s) => new()
        {
            Id = s.Id,
            Name = s.Name,
            AttributeDeltas = s.AttributeDeltas
                .Select(d => new AttributeDelta(d.AttributeName, d.Delta))
                .ToImmutableList(),
            MaxHpDelta = s.MaxHpDelta,
            OnApplyHpDelta = s.OnApplyHpDelta,
            Notes = s.Notes,
            AppliedUtc = s.AppliedUtc,
        };

        public static AttributeValue ToAttributeValue(AttributeValueSnapshot snap) => snap.Type switch
        {
            AttributeValueType.Score => AttributeValue.Score(snap.IntValue ?? 10),
            AttributeValueType.Modifier => AttributeValue.Modifier(snap.IntValue ?? 0),
            AttributeValueType.Text => AttributeValue.Text(snap.StringValue ?? string.Empty),
            _ => AttributeValue.Score(10),
        };

        public static AttributeSchema ToAttributeSchema(AttributeSchemaSnapshot snap)
        {
            // Built-in presets always rebuild from FromPreset so future preset
            // definition changes flow into existing snapshots. The trade-off:
            // in-place row edits to a built-in preset are intentionally not
            // persisted — switch the schema to Custom if you want edits to
            // survive a reload.
            if (snap.Preset != AttributePreset.Custom)
                return AttributeSchema.FromPreset(snap.Preset);

            var rows = snap.Rows
                .Select(r => new AttributeRow(r.Name, r.Type, ToAttributeValue(r.Default)))
                .ToList();
            return new AttributeSchema(AttributePreset.Custom, rows);
        }

        private static AttributeSchemaSnapshot ToSchemaSnapshot(AttributeSchema schema) => new()
        {
            Preset = schema.Preset,
            Rows = schema.Rows
                .Select(r => new AttributeRowSnapshot
                {
                    Name = r.Name,
                    Type = r.Type,
                    Default = ToValueSnapshot(r.Default),
                })
                .ToList(),
        };

        internal static AttributeValueSnapshot ToValueSnapshot(AttributeValue value) => new()
        {
            Type = value.Type,
            IntValue = value.IntValue,
            StringValue = value.StringValue,
        };

        internal static MapImageSnapshot ToImageSnapshot(MapImage image) => new()
        {
            Id = image.Id,
            Name = image.Name,
            ContentType = image.ContentType,
            X = image.X,
            Y = image.Y,
            Width = image.Width,
            Height = image.Height,
            OriginalWidth = image.OriginalWidth,
            OriginalHeight = image.OriginalHeight,
            Rotation = image.Rotation,
            Opacity = image.Opacity,
            LayerOrder = image.LayerOrder,
            Locked = image.Locked,
            Hidden = image.Hidden,
            ByteSize = image.ByteSize,
            WasDownscaled = image.WasDownscaled,
            OriginalLongEdgePx = image.OriginalLongEdgePx,
            DisplayLongEdgePx = image.DisplayLongEdgePx,
        };

        internal static TokenSnapshot ToTokenSnapshot(Token token) => new()
        {
            Id = token.Id,
            Name = token.Name,
            Color = token.Color,
            IconKind = token.IconKind,
            MapId = token.MapId,
            X = token.X,
            Y = token.Y,
            SheetId = token.SheetId,
            Hidden = token.Hidden,
        };

        public static SheetSnapshot ToSheetSnapshot(CharacterSheet sheet) => new()
        {
            Id = sheet.Id,
            CharacterName = sheet.CharacterName,
            Values = sheet.Values.ToDictionary(kv => kv.Key, kv => ToValueSnapshot(kv.Value)),
            Notes = sheet.Notes,
            Hp = sheet.Hp,
            MaxHp = sheet.MaxHp,
            ArmorClass = sheet.ArmorClass,
            Color = sheet.Color,
            ScopedMapId = sheet.ScopedMapId,
            StatusEffects = sheet.StatusEffects.Select(ToStatusEffectSnapshot).ToList(),
            RollTemplates = sheet.RollTemplates.Select(ToRollTemplateSnapshot).ToList(),
        };
    }
}
