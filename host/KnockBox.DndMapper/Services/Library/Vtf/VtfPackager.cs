using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.DndMapper.Services.Logic.Games;

namespace KnockBox.DndMapper.Services.Library.Vtf
{
    /// <summary>
    /// Pure ZIP pack/unpack between a slot's snapshot shards (+ image bytes)
    /// and a `.vtf` archive per the Virtual Table Format spec v1.0.0.
    /// <para>
    /// DnD Mapper-specific fields ride in <c>vendorData.knockbox_dnd_mapper</c>
    /// blocks; the spec's <c>layers</c> / <c>entityInstances</c> projections
    /// give foreign parsers a usable read of position and asset references.
    /// Round-trip authority on import is the vendor data — spec projections
    /// are regenerated on every pack.
    /// </para>
    /// </summary>
    internal static class VtfPackager
    {
        public const string SpecVersion = "1.0.0";
        public const string VendorKey = "knockbox_dnd_mapper";
        public const int SupportedMajorVersion = 1;

        private const string ManifestEntryName = "manifest.json";
        private const string GlobalStateEntryName = "global_state.json";
        private const string ScenesPrefix = "scenes/";
        private const string EntitiesPrefix = "entities/";
        private const string ImagesPrefix = "assets/images/";
        private const string ExtensionsPrefix = "extensions/";
        private const string ExtensionEntryName = ExtensionsPrefix + VendorKey + ".json";
        private const string SheetEntityPrefix = "sheet_";

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };
        private static readonly JsonSerializerOptions JsonIndented = new(Json) { WriteIndented = true };

        public sealed record PackInput(
            string SlotTitle,
            LibraryCoreSnapshot Core,
            IReadOnlyList<MapSnapshot> Maps,
            IReadOnlyList<SheetSnapshot> Sheets,
            IReadOnlyDictionary<Guid, VtfImageAsset> Images,
            VtfExtensionPayload Extension);

        public sealed record VtfImageAsset(string ContentType, byte[] Bytes);

        public sealed record VtfExtensionPayload(
            State.Games.Data.CombatState? ActiveCombat,
            Models.DndMapperPhase Phase);

        public sealed record UnpackResult(
            string SlotTitle,
            LibraryCoreSnapshot Core,
            IReadOnlyList<MapSnapshot> Maps,
            IReadOnlyList<SheetSnapshot> Sheets,
            IReadOnlyDictionary<Guid, VtfImageAsset> Images,
            VtfExtensionPayload Extension,
            IReadOnlyList<string> Warnings);

        /// <summary>One JSON entry destined for a VTF archive entry path.</summary>
        public sealed record ClientPayloadEntry(string Path, string Content);

        /// <summary>Per-image reference for the client packer to fetch from IndexedDB.</summary>
        public sealed record ClientImageRef(string Id, string Path);

        /// <summary>
        /// JSON-only export payload. Same JSON entries as <see cref="Pack"/>
        /// would write into the archive, plus a list of image refs the client
        /// is expected to read out of IndexedDB and embed (uncompressed) at
        /// the listed paths. Image bytes intentionally never cross the SignalR
        /// boundary — this is the whole point of the client-side packer.
        /// </summary>
        public sealed record ClientPayload(
            string SlotName,
            string FileName,
            IReadOnlyList<ClientPayloadEntry> Entries,
            IReadOnlyList<ClientImageRef> Images);

        /// <summary>
        /// Builds the JSON shards a VTF archive needs. The caller (typically
        /// a JS-side packer) is responsible for assembling the ZIP and
        /// attaching the binary image entries — this method intentionally
        /// returns NO image bytes so a slot with hundreds of MB of map art
        /// produces only a small JSON payload at the boundary.
        /// </summary>
        public static ClientPayload BuildClientPayload(
            string slotTitle,
            string fileName,
            LibraryCoreSnapshot core,
            IReadOnlyList<MapSnapshot> maps,
            IReadOnlyList<SheetSnapshot> sheets,
            IReadOnlyDictionary<Guid, string> imageContentTypes,
            VtfExtensionPayload extension)
        {
            ArgumentNullException.ThrowIfNull(core);
            ArgumentNullException.ThrowIfNull(maps);
            ArgumentNullException.ThrowIfNull(sheets);
            ArgumentNullException.ThrowIfNull(imageContentTypes);

            var imageExtById = new Dictionary<Guid, string>(imageContentTypes.Count);
            foreach (var (id, contentType) in imageContentTypes)
                imageExtById[id] = ExtensionForContentType(contentType);

            // Mirrors Pack's iteration order — manifest → global → scenes →
            // entities → extension. The extension is intentionally written
            // last for symmetry with the server-side archive; ZIP entry
            // order doesn't affect VTF correctness but keeps the on-disk
            // bytes recognizable across both code paths.
            var entries = new List<ClientPayloadEntry>(2 + maps.Count + sheets.Count + 1);

            var packInput = new PackInput(slotTitle, core, maps, sheets,
                ImageEnumerableAsEmptyDictionary(), extension);
            entries.Add(new ClientPayloadEntry(ManifestEntryName, SerializeIndented(BuildManifest(packInput))));
            entries.Add(new ClientPayloadEntry(GlobalStateEntryName, SerializeIndented(BuildGlobalState(core))));

            foreach (var map in maps)
                entries.Add(new ClientPayloadEntry(
                    $"{ScenesPrefix}{map.Id:D}.json",
                    SerializeIndented(BuildScene(map, imageExtById))));

            foreach (var sheet in sheets)
                entries.Add(new ClientPayloadEntry(
                    $"{EntitiesPrefix}{SheetEntityPrefix}{sheet.Id:D}.json",
                    SerializeIndented(BuildEntity(sheet))));

            entries.Add(new ClientPayloadEntry(ExtensionEntryName, SerializeIndented(extension)));

            var imageRefs = new List<ClientImageRef>(imageExtById.Count);
            foreach (var (id, ext) in imageExtById)
                imageRefs.Add(new ClientImageRef(id.ToString("D"), $"{ImagesPrefix}{id:D}{ext}"));

            return new ClientPayload(slotTitle, fileName, entries, imageRefs);
        }

        // Pack's PackInput requires an Images dict purely for ExtensionForContentType
        // resolution; BuildClientPayload supplies the content-type map directly so
        // it doesn't need image bytes. This helper hands BuildManifest an empty
        // dictionary it never reads.
        private static IReadOnlyDictionary<Guid, VtfImageAsset> ImageEnumerableAsEmptyDictionary()
            => new Dictionary<Guid, VtfImageAsset>();

        private static string SerializeIndented<T>(T value) =>
            JsonSerializer.Serialize(value, JsonIndented);

        public static void Pack(PackInput input, Stream destination)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(destination);

            using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

            var imageExtById = new Dictionary<Guid, string>(input.Images.Count);
            foreach (var (id, asset) in input.Images)
                imageExtById[id] = ExtensionForContentType(asset.ContentType);

            WriteJsonEntry(zip, ManifestEntryName, BuildManifest(input));
            WriteJsonEntry(zip, GlobalStateEntryName, BuildGlobalState(input.Core));

            foreach (var map in input.Maps)
                WriteJsonEntry(zip, $"{ScenesPrefix}{map.Id:D}.json", BuildScene(map, imageExtById));

            foreach (var sheet in input.Sheets)
                WriteJsonEntry(zip, $"{EntitiesPrefix}{SheetEntityPrefix}{sheet.Id:D}.json", BuildEntity(sheet));

            foreach (var (id, asset) in input.Images)
            {
                var entry = zip.CreateEntry($"{ImagesPrefix}{id:D}{imageExtById[id]}", CompressionLevel.NoCompression);
                using var s = entry.Open();
                s.Write(asset.Bytes, 0, asset.Bytes.Length);
            }

            WriteJsonEntry(zip, ExtensionEntryName, input.Extension);
        }

        public static UnpackResult Unpack(Stream source)
        {
            ArgumentNullException.ThrowIfNull(source);

            using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);

            foreach (var entry in zip.Entries)
            {
                if (!IsSafeRelativePath(entry.FullName))
                    throw new InvalidDataException($"Unsafe archive entry path: {entry.FullName}");
            }

            var manifest = ReadJsonEntry<VtfManifest>(zip, ManifestEntryName)
                ?? throw new InvalidDataException("Missing manifest.json.");

            if (!TryParseMajorVersion(manifest.VtfVersion, out var major))
                throw new InvalidDataException($"Unrecognized vtfVersion: '{manifest.VtfVersion}'.");
            if (major > SupportedMajorVersion)
                throw new InvalidDataException(
                    $"This .vtf was written for VTF v{major}.x; this build supports up to v{SupportedMajorVersion}.x.");

            var global = ReadJsonEntry<VtfGlobalState>(zip, GlobalStateEntryName) ?? new VtfGlobalState();
            var globalVendor = ReadVendor<DndMapperGlobalVendor>(global.VendorData) ?? new DndMapperGlobalVendor();

            var warnings = new List<string>();

            var images = new Dictionary<Guid, VtfImageAsset>();
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith(ImagesPrefix, StringComparison.Ordinal)) continue;
                var fileName = entry.FullName[ImagesPrefix.Length..];
                if (fileName.Length == 0 || fileName.EndsWith('/')) continue;
                var stem = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName);
                if (!Guid.TryParseExact(stem, "D", out var id))
                {
                    warnings.Add($"Skipped image with non-GUID filename: {fileName}");
                    continue;
                }
                var ct = ContentTypeForExtension(ext);
                if (ct is null)
                {
                    warnings.Add($"Skipped image with unsupported extension: {fileName}");
                    continue;
                }
                using var s = entry.Open();
                using var ms = new MemoryStream(checked((int)Math.Min(entry.Length, int.MaxValue)));
                s.CopyTo(ms);
                images[id] = new VtfImageAsset(ct, ms.ToArray());
            }

            var maps = new List<MapSnapshot>();
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith(ScenesPrefix, StringComparison.Ordinal)) continue;
                if (!entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                var scene = ReadJsonEntry<VtfScene>(zip, entry.FullName);
                if (scene is null) continue;
                if (!TryBuildMapSnapshotFromScene(scene, images, warnings, out var mapSnap)) continue;
                maps.Add(mapSnap);
            }

            // Re-order maps using the explicit MapOrder in vendor data when present;
            // otherwise sort by the per-map ListOrder.
            if (globalVendor.MapOrder.Count > 0)
            {
                var index = globalVendor.MapOrder.Select((id, i) => (id, i))
                    .ToDictionary(p => p.id, p => p.i);
                maps = maps.OrderBy(m => index.TryGetValue(m.Id, out var rank) ? rank : int.MaxValue)
                           .ThenBy(m => m.ListOrder)
                           .ToList();
            }
            else
            {
                maps = maps.OrderBy(m => m.ListOrder).ToList();
            }

            var sheets = new List<SheetSnapshot>();
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith(EntitiesPrefix, StringComparison.Ordinal)) continue;
                if (!entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                var entity = ReadJsonEntry<VtfEntity>(zip, entry.FullName);
                if (entity is null) continue;
                var sheetSnap = ReadVendor<SheetSnapshot>(entity.VendorData);
                if (sheetSnap is null)
                {
                    warnings.Add($"Skipped non-DnD-Mapper entity: {entry.FullName}");
                    continue;
                }
                sheets.Add(sheetSnap);
            }

            if (globalVendor.SheetOrder.Count > 0)
            {
                var index = globalVendor.SheetOrder.Select((id, i) => (id, i))
                    .ToDictionary(p => p.id, p => p.i);
                sheets = sheets.OrderBy(s => index.TryGetValue(s.Id, out var rank) ? rank : int.MaxValue).ToList();
            }

            var core = new LibraryCoreSnapshot
            {
                SchemaVersion = 4,
                Settings = globalVendor.Settings,
                AttributeSchema = globalVendor.AttributeSchema,
                ActiveSchemaTemplateId = globalVendor.ActiveSchemaTemplateId,
                InitiativeAttributeName = globalVendor.InitiativeAttributeName,
                CustomTemplates = globalVendor.CustomTemplates,
                GlobalRollTemplates = globalVendor.GlobalRollTemplates,
                MapIds = maps.Select(m => m.Id).ToList(),
                SheetIds = sheets.Select(s => s.Id).ToList(),
            };

            var extension = ReadJsonEntry<VtfExtensionPayload>(zip, ExtensionEntryName)
                ?? new VtfExtensionPayload(null, KnockBox.DndMapper.Models.DndMapperPhase.Lobby);

            var title = string.IsNullOrWhiteSpace(manifest.Campaign.Title)
                ? "Imported slot"
                : manifest.Campaign.Title;

            return new UnpackResult(title, core, maps, sheets, images, extension, warnings);
        }

        public static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.Contains('\\')) return false;
            if (path.StartsWith('/')) return false;
            if (path.Length >= 2 && path[1] == ':') return false; // C:/, D:/...
            foreach (var seg in path.Split('/'))
            {
                if (seg == ".." || seg == ".") return false;
            }
            return true;
        }

        // ── Pack helpers ──────────────────────────────────────────────────

        private static VtfManifest BuildManifest(PackInput input) => new()
        {
            VtfVersion = SpecVersion,
            Campaign = new VtfCampaign
            {
                Id = Guid.NewGuid().ToString("D"),
                Title = input.SlotTitle,
                Author = null,
                LastModified = DateTime.UtcNow,
            },
            System = new VtfSystem { Core = "dnd5e" },
            Dependencies = [new VtfDependency { Name = VendorKey, MinVersion = "1" }],
            EntryState = input.Maps.Count > 0
                ? new VtfEntryState { ActiveScene = $"{ScenesPrefix}{input.Maps[0].Id:D}.json" }
                : null,
        };

        private static VtfGlobalState BuildGlobalState(LibraryCoreSnapshot core) => new()
        {
            CampaignTime = new Dictionary<string, JsonElement>(),
            Playlist = new List<JsonElement>(),
            VendorData = WrapVendor(new DndMapperGlobalVendor
            {
                Settings = core.Settings,
                AttributeSchema = core.AttributeSchema,
                ActiveSchemaTemplateId = core.ActiveSchemaTemplateId,
                InitiativeAttributeName = core.InitiativeAttributeName,
                CustomTemplates = core.CustomTemplates,
                GlobalRollTemplates = core.GlobalRollTemplates,
                MapOrder = core.MapIds,
                SheetOrder = core.SheetIds,
            }),
        };

        private static VtfScene BuildScene(MapSnapshot map, IReadOnlyDictionary<Guid, string> imageExtById)
        {
            var widthPx = Math.Max(1, map.Grid.WidthCells * map.Grid.CellPixels);
            var heightPx = Math.Max(1, map.Grid.HeightCells * map.Grid.CellPixels);

            var layers = map.Images
                .OrderBy(i => i.LayerOrder)
                .Select(i => new VtfLayer
                {
                    Id = i.Id.ToString("D"),
                    Name = i.Name,
                    Type = "image",
                    AssetRef = imageExtById.TryGetValue(i.Id, out var ext)
                        ? $"{ImagesPrefix}{i.Id:D}{ext}"
                        : null,
                    ZIndex = i.LayerOrder,
                    Opacity = i.Opacity,
                    VendorData = WrapVendor(i),
                })
                .ToList();

            var instances = map.Tokens
                .Select(t => new VtfEntityInstance
                {
                    InstanceId = t.Id.ToString("D"),
                    EntityRef = t.SheetId is { } sid
                        ? $"{EntitiesPrefix}{SheetEntityPrefix}{sid:D}.json"
                        : null,
                    Transform = new VtfTransform
                    {
                        GridPosition = new VtfGridPosition { X = t.X, Y = t.Y },
                        PixelOffset = new VtfPixelOffset { X = 0, Y = 0 },
                        Rotation = 0,
                        Scale = 1.0,
                    },
                    VendorData = WrapVendor(t),
                })
                .ToList();

            return new VtfScene
            {
                SceneId = map.Id.ToString("D"),
                Dimensions = new VtfDimensions { Width = widthPx, Height = heightPx },
                Grid = new VtfGrid
                {
                    Type = "square",
                    Size = map.Grid.CellPixels,
                    OffsetX = 0,
                    OffsetY = 0,
                    Color = map.Grid.LineColor,
                    Visible = map.Grid.ShowGridLines,
                    Measurement = new VtfGridMeasurement { Distance = 5, Unit = "ft" },
                },
                Ambience = new List<JsonElement>(),
                Layers = layers,
                EntityInstances = instances,
                VendorData = WrapVendor(new DndMapperSceneVendor
                {
                    Id = map.Id,
                    Name = map.Name,
                    ListOrder = map.ListOrder,
                    CreatedUtc = map.CreatedUtc,
                    Grid = map.Grid,
                    DefaultSpawnX = map.DefaultSpawnX,
                    DefaultSpawnY = map.DefaultSpawnY,
                    FogMask = map.FogMask,
                }),
            };
        }

        private static VtfEntity BuildEntity(SheetSnapshot sheet) => new()
        {
            EntityId = $"{SheetEntityPrefix}{sheet.Id:D}",
            Name = sheet.CharacterName,
            AssetRef = null,
            VendorData = WrapVendor(sheet),
        };

        // ── Unpack helpers ────────────────────────────────────────────────

        private static bool TryBuildMapSnapshotFromScene(
            VtfScene scene,
            IReadOnlyDictionary<Guid, VtfImageAsset> images,
            List<string> warnings,
            out MapSnapshot result)
        {
            var sceneVendor = ReadVendor<DndMapperSceneVendor>(scene.VendorData);
            if (sceneVendor is null || sceneVendor.Id == Guid.Empty)
            {
                warnings.Add($"Skipped scene without DnD Mapper vendor data: {scene.SceneId}");
                result = default!;
                return false;
            }

            var imageList = new List<MapImageSnapshot>(scene.Layers.Count);
            foreach (var layer in scene.Layers)
            {
                if (!string.Equals(layer.Type, "image", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"Skipped layer with unsupported type '{layer.Type}'.");
                    continue;
                }
                var snap = ReadVendor<MapImageSnapshot>(layer.VendorData);
                if (snap is null || snap.Id == Guid.Empty)
                {
                    warnings.Add($"Skipped layer without DnD Mapper vendor data: {layer.Id}");
                    continue;
                }
                if (!images.ContainsKey(snap.Id))
                {
                    warnings.Add($"Skipped layer with missing image binary: {snap.Id:D}");
                    continue;
                }
                imageList.Add(snap);
            }

            var tokenList = new List<TokenSnapshot>(scene.EntityInstances.Count);
            foreach (var inst in scene.EntityInstances)
            {
                var snap = ReadVendor<TokenSnapshot>(inst.VendorData);
                if (snap is null || snap.Id == Guid.Empty)
                {
                    // Fallback: synthesize a minimal token from spec fields so
                    // foreign-generated VTFs still produce something visible.
                    if (inst.Transform?.GridPosition is { } gp
                        && Guid.TryParseExact(inst.InstanceId, "D", out var instId))
                    {
                        tokenList.Add(new TokenSnapshot
                        {
                            Id = instId,
                            MapId = sceneVendor.Id,
                            X = gp.X,
                            Y = gp.Y,
                        });
                    }
                    continue;
                }
                tokenList.Add(snap);
            }

            result = new MapSnapshot
            {
                Id = sceneVendor.Id,
                Name = sceneVendor.Name,
                ListOrder = sceneVendor.ListOrder,
                CreatedUtc = sceneVendor.CreatedUtc,
                Grid = sceneVendor.Grid,
                DefaultSpawnX = sceneVendor.DefaultSpawnX,
                DefaultSpawnY = sceneVendor.DefaultSpawnY,
                Images = imageList,
                Tokens = tokenList,
                FogMask = sceneVendor.FogMask ?? [],
            };
            return true;
        }

        // ── JSON / archive helpers ────────────────────────────────────────

        private static void WriteJsonEntry<T>(ZipArchive zip, string entryName, T payload)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var s = entry.Open();
            JsonSerializer.Serialize(s, payload, JsonIndented);
        }

        private static T? ReadJsonEntry<T>(ZipArchive zip, string entryName)
        {
            var entry = zip.GetEntry(entryName);
            if (entry is null) return default;
            using var s = entry.Open();
            return JsonSerializer.Deserialize<T>(s, Json);
        }

        private static Dictionary<string, JsonElement> WrapVendor<T>(T payload)
        {
            var element = JsonSerializer.SerializeToElement(payload, Json);
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [VendorKey] = element,
            };
        }

        private static T? ReadVendor<T>(Dictionary<string, JsonElement>? vendorData)
        {
            if (vendorData is null) return default;
            if (!vendorData.TryGetValue(VendorKey, out var element)) return default;
            return element.Deserialize<T>(Json);
        }

        private static bool TryParseMajorVersion(string version, out int major)
        {
            major = 0;
            if (string.IsNullOrWhiteSpace(version)) return false;
            var dot = version.IndexOf('.');
            var head = dot < 0 ? version : version[..dot];
            return int.TryParse(head, out major) && major > 0;
        }

        public static string ExtensionForContentType(string contentType) =>
            contentType?.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                "image/webp" => ".webp",
                _ => ".bin",
            };

        private static string? ContentTypeForExtension(string ext) =>
            ext?.ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => null,
            };
    }

    // DnD Mapper-specific vendor payloads. Round-trip authority on import is
    // these records (the spec's layers / entityInstances projections exist for
    // foreign parsers; we ignore them on read).

    internal sealed record DndMapperGlobalVendor
    {
        public State.Games.Data.DndMapperSettings Settings { get; init; } = new();
        public AttributeSchemaSnapshot AttributeSchema { get; init; } = new();
        public Guid? ActiveSchemaTemplateId { get; init; }
        public string? InitiativeAttributeName { get; init; }
        public List<NamedTemplateSnapshot> CustomTemplates { get; init; } = [];
        public List<RollTemplateSnapshot> GlobalRollTemplates { get; init; } = [];
        public List<Guid> MapOrder { get; init; } = [];
        public List<Guid> SheetOrder { get; init; } = [];
    }

    internal sealed record DndMapperSceneVendor
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int ListOrder { get; init; }
        public DateTime CreatedUtc { get; init; }
        public State.Games.Data.GridConfig Grid { get; init; } = new();
        public double? DefaultSpawnX { get; init; }
        public double? DefaultSpawnY { get; init; }
        public byte[] FogMask { get; init; } = [];
    }
}
