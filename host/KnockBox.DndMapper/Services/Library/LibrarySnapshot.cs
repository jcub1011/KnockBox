using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.State.Games.Data;

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
        public int SchemaVersion { get; init; } = 1;
        public DndMapperSettings Settings { get; init; } = new();
        public AttributeSchemaSnapshot AttributeSchema { get; init; } = new();
        public List<MapSnapshot> Maps { get; init; } = [];
        public List<SheetSnapshot> Sheets { get; init; } = [];
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
    }

    internal sealed record MapImageSnapshot
    {
        public Guid Id { get; init; }
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
        public long ByteSize { get; init; }
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
            var maps = new List<MapSnapshot>(state.Maps.Count);
            foreach (var map in state.Maps.OrderBy(m => m.ListOrder))
            {
                maps.Add(new MapSnapshot
                {
                    Id = map.Id,
                    Name = map.Name,
                    ListOrder = map.ListOrder,
                    CreatedUtc = map.CreatedUtc,
                    Grid = map.Grid.Clone(),
                    DefaultSpawnX = map.DefaultSpawnPosition?.X,
                    DefaultSpawnY = map.DefaultSpawnPosition?.Y,
                    Images = map.Images
                        .OrderBy(i => i.LayerOrder)
                        .Select(ToImageSnapshot)
                        .ToList(),
                    Tokens = map.Tokens
                        .Select(ToTokenSnapshot)
                        .ToList(),
                });
            }

            var sheets = state.Sheets.Values
                .Select(ToSheetSnapshot)
                .ToList();

            return new LibrarySnapshot
            {
                SchemaVersion = 1,
                Settings = state.Settings.Clone(),
                AttributeSchema = ToSchemaSnapshot(state.AttributeSchema),
                Maps = maps,
                Sheets = sheets,
            };
        }

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

        private static AttributeValueSnapshot ToValueSnapshot(AttributeValue value) => new()
        {
            Type = value.Type,
            IntValue = value.IntValue,
            StringValue = value.StringValue,
        };

        private static MapImageSnapshot ToImageSnapshot(MapImage image) => new()
        {
            Id = image.Id,
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
            ByteSize = image.ByteSize,
        };

        private static TokenSnapshot ToTokenSnapshot(Token token) => new()
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

        private static SheetSnapshot ToSheetSnapshot(CharacterSheet sheet) => new()
        {
            Id = sheet.Id,
            CharacterName = sheet.CharacterName,
            Values = sheet.Values.ToDictionary(kv => kv.Key, kv => ToValueSnapshot(kv.Value)),
            Notes = sheet.Notes,
            Hp = sheet.Hp,
            MaxHp = sheet.MaxHp,
        };
    }
}
