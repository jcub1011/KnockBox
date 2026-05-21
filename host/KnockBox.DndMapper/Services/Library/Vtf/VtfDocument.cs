using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnockBox.DndMapper.Services.Library.Vtf
{
    // DTOs for the Virtual Table Format (.vtf) spec, v1.0.0. JsonExtensionData
    // catches unknown fields so a future minor revision can round-trip through
    // us without losing data; VendorData carries app-specific payloads under
    // the namespacing rule from spec §5.
    //
    // Property names are PascalCase here but get serialized as camelCase via
    // the global JsonSerializerOptions configured in VtfPackager.

    internal sealed class VtfManifest
    {
        public string VtfVersion { get; set; } = "1.0.0";
        public VtfCampaign Campaign { get; set; } = new();
        public VtfSystem System { get; set; } = new();
        public List<VtfDependency> Dependencies { get; set; } = [];
        public VtfEntryState? EntryState { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed class VtfCampaign
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Author { get; set; }
        public DateTime LastModified { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed class VtfSystem
    {
        public string Core { get; set; } = string.Empty;

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed class VtfDependency
    {
        public string Name { get; set; } = string.Empty;
        public string? MinVersion { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed class VtfEntryState
    {
        public string? ActiveScene { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed class VtfGlobalState
    {
        public Dictionary<string, JsonElement>? CampaignTime { get; set; }
        public List<JsonElement>? Playlist { get; set; }
        public Dictionary<string, JsonElement>? VendorData { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed class VtfScene
    {
        public string SceneId { get; set; } = string.Empty;
        public VtfDimensions Dimensions { get; set; } = new();
        public VtfGrid Grid { get; set; } = new();
        public List<JsonElement>? Ambience { get; set; }
        public List<VtfLayer> Layers { get; set; } = [];
        public List<VtfEntityInstance> EntityInstances { get; set; } = [];
        public Dictionary<string, JsonElement>? VendorData { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed class VtfDimensions
    {
        public int Width { get; set; }
        public int Height { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed class VtfGrid
    {
        public string Type { get; set; } = "square";
        public int Size { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public string? Color { get; set; }
        public bool Visible { get; set; } = true;
        public VtfGridMeasurement? Measurement { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed class VtfGridMeasurement
    {
        public double Distance { get; set; }
        public string Unit { get; set; } = "ft";

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed class VtfLayer
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string Type { get; set; } = "image";
        public string? AssetRef { get; set; }
        public int ZIndex { get; set; }
        public double Opacity { get; set; } = 1.0;
        public Dictionary<string, JsonElement>? VendorData { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed class VtfEntityInstance
    {
        public string InstanceId { get; set; } = string.Empty;
        public string? EntityRef { get; set; }
        public VtfTransform Transform { get; set; } = new();
        public Dictionary<string, JsonElement>? LocalOverrides { get; set; }
        public Dictionary<string, JsonElement>? VendorData { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed class VtfTransform
    {
        public VtfGridPosition? GridPosition { get; set; }
        public VtfPixelOffset? PixelOffset { get; set; }
        public double Rotation { get; set; }
        public double Scale { get; set; } = 1.0;

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed class VtfGridPosition
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    internal sealed class VtfPixelOffset
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    internal sealed class VtfEntity
    {
        public string EntityId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? AssetRef { get; set; }
        public Dictionary<string, JsonElement>? AudioEmitter { get; set; }
        public Dictionary<string, JsonElement>? VendorData { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }
}
