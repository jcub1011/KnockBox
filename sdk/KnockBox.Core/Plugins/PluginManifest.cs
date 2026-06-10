using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using KnockBox.Core.Primitives.Returns;

namespace KnockBox.Core.Plugins;

/// <summary>
/// Default <see cref="IPluginManifest"/> implementation plus a parser that reads
/// <c>plugin.json</c> from a file, stream, or embedded assembly resource.
/// </summary>
public sealed partial record PluginManifest(
    string Name,
    string Description,
    string RouteIdentifier,
    Version Version,
    string EntryAssembly,
    IReadOnlySet<PluginCapability> Capabilities,
    string? TileAsset = null,
    bool WorkInProgress = false,
    PluginKind Kind = PluginKind.Game,
    IReadOnlyList<string>? ExportedContracts = null,
    string? BackgroundColor = null,
    string? FontColor = null) : IPluginManifest
{
    /// <inheritdoc/>
    public IReadOnlyList<string> ExportedContracts { get; init; } =
        ExportedContracts ?? Array.Empty<string>();

    /// <summary>
    /// The one supported <c>plugin.json</c> schema version. Bump (and add
    /// migration handling) if the on-disk shape changes.
    /// </summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>
    /// Name of the embedded resource the default loader helper looks for.
    /// Plugins that want <see cref="FromEmbeddedResource(Assembly)"/> to work
    /// must embed their <c>plugin.json</c> under a resource name ending with
    /// this suffix (case-insensitive).
    /// </summary>
    public const string EmbeddedResourceName = "plugin.json";

    [GeneratedRegex(@"^[a-z0-9-]+$")]
    private static partial Regex RouteIdentifierPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex ExportedContractNamePattern();

    [GeneratedRegex(@"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColorPattern();

    private static readonly FrozenDictionary<string, PluginCapability> CapabilityByName =
        new Dictionary<string, PluginCapability>(StringComparer.OrdinalIgnoreCase)
        {
            ["config"] = PluginCapability.Config,
            ["storage"] = PluginCapability.Storage,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, PluginKind> KindByName =
        new Dictionary<string, PluginKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["game"] = PluginKind.Game,
            ["library"] = PluginKind.Library,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a <c>plugin.json</c> file and returns a parsed manifest. File I/O
    /// and JSON parse failures are surfaced as a failure result rather than an
    /// exception so the loader can attribute them to the offending plugin and
    /// keep scanning siblings.
    /// </summary>
    public static ValueResult<PluginManifest> TryReadFromFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return TryParse(stream);
        }
        catch (FileNotFoundException)
        {
            return ValueResult<PluginManifest>.FromError($"Manifest file [{path}] not found.");
        }
        catch (IOException ex)
        {
            return ValueResult<PluginManifest>.FromError($"Failed to read manifest [{path}]: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return ValueResult<PluginManifest>.FromError($"Access denied reading manifest [{path}]: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads the plugin manifest embedded in <paramref name="assembly"/> and
    /// throws on any failure. Intended for use from an <see cref="IGameModule"/>
    /// property initializer where a missing or malformed manifest is a
    /// non-recoverable programmer error.
    /// </summary>
    public static PluginManifest FromEmbeddedResourceOrThrow(Assembly assembly)
    {
        var result = FromEmbeddedResource(assembly);
        if (result.TryGetSuccess(out var manifest))
            return manifest;
        result.TryGetFailure(out var error);
        throw new InvalidOperationException(
            $"Failed to load embedded plugin.json from assembly [{assembly.GetName().Name}]: {error.PublicMessage}");
    }

    /// <summary>
    /// Reads the plugin manifest embedded in <paramref name="assembly"/>. The
    /// resource must end with <see cref="EmbeddedResourceName"/>
    /// (case-insensitive) — MSBuild typically produces
    /// <c>{DefaultNamespace}.plugin.json</c>.
    /// </summary>
    public static ValueResult<PluginManifest> FromEmbeddedResource(Assembly assembly)
    {
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(EmbeddedResourceName, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            return ValueResult<PluginManifest>.FromError(
                $"Assembly [{assembly.GetName().Name}] has no embedded [{EmbeddedResourceName}] resource. " +
                $"Ensure the plugin project includes plugin.json as <EmbeddedResource>.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return ValueResult<PluginManifest>.FromError(
                $"Assembly [{assembly.GetName().Name}] declares embedded resource [{resourceName}] but the stream could not be opened.");
        }

        return TryParse(stream);
    }

    /// <summary>
    /// Parses a <c>plugin.json</c> document from a stream. Validates
    /// <c>schemaVersion</c>, route-identifier shape, version parseability,
    /// required fields, and that every capability string maps to a known
    /// <see cref="PluginCapability"/>.
    /// </summary>
    public static ValueResult<PluginManifest> TryParse(Stream stream)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(stream);
        }
        catch (JsonException ex)
        {
            return ValueResult<PluginManifest>.FromError($"Malformed plugin.json: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ValueResult<PluginManifest>.FromError("plugin.json root must be a JSON object.");

            if (!root.TryGetProperty("schemaVersion", out var schemaVersionElement)
                || schemaVersionElement.ValueKind != JsonValueKind.Number
                || !schemaVersionElement.TryGetInt32(out var schemaVersion))
            {
                return ValueResult<PluginManifest>.FromError("plugin.json is missing required integer 'schemaVersion'.");
            }

            if (schemaVersion != SupportedSchemaVersion)
            {
                return ValueResult<PluginManifest>.FromError(
                    $"plugin.json schemaVersion [{schemaVersion}] is not supported (expected [{SupportedSchemaVersion}]).");
            }

            if (!TryRequireString(root, "name", out var name, out var nameError))
                return ValueResult<PluginManifest>.FromError(nameError);
            if (!TryRequireString(root, "description", out var description, out var descError))
                return ValueResult<PluginManifest>.FromError(descError);
            if (!TryRequireString(root, "routeIdentifier", out var routeIdentifier, out var routeError))
                return ValueResult<PluginManifest>.FromError(routeError);
            if (!TryRequireString(root, "version", out var versionString, out var versionError))
                return ValueResult<PluginManifest>.FromError(versionError);
            if (!TryRequireString(root, "entryAssembly", out var entryAssembly, out var entryError))
                return ValueResult<PluginManifest>.FromError(entryError);

            if (!RouteIdentifierPattern().IsMatch(routeIdentifier))
            {
                return ValueResult<PluginManifest>.FromError(
                    $"plugin.json routeIdentifier [{routeIdentifier}] must match ^[a-z0-9-]+$.");
            }

            if (!Version.TryParse(versionString, out var version))
            {
                return ValueResult<PluginManifest>.FromError(
                    $"plugin.json version [{versionString}] is not a valid System.Version string.");
            }

            var capabilities = new HashSet<PluginCapability>();
            if (root.TryGetProperty("capabilities", out var capabilitiesElement))
            {
                if (capabilitiesElement.ValueKind != JsonValueKind.Array)
                {
                    return ValueResult<PluginManifest>.FromError(
                        "plugin.json 'capabilities' must be an array of strings.");
                }

                foreach (var entry in capabilitiesElement.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.String)
                    {
                        return ValueResult<PluginManifest>.FromError(
                            "plugin.json 'capabilities' entries must be strings.");
                    }

                    var raw = entry.GetString()!;
                    if (!CapabilityByName.TryGetValue(raw, out var capability))
                    {
                        return ValueResult<PluginManifest>.FromError(
                            $"plugin.json declares unknown capability [{raw}]. Known capabilities: " +
                            string.Join(", ", CapabilityByName.Keys) + ".");
                    }

                    capabilities.Add(capability);
                }
            }

            string? tileAsset = null;
            if (root.TryGetProperty("tileAsset", out var tileAssetElement)
                && tileAssetElement.ValueKind != JsonValueKind.Null)
            {
                if (tileAssetElement.ValueKind != JsonValueKind.String)
                {
                    return ValueResult<PluginManifest>.FromError(
                        "plugin.json 'tileAsset' must be a string.");
                }

                var raw = tileAssetElement.GetString()!;
                if (!TryValidateTileAsset(raw, out var validatedTileAsset, out var tileAssetError))
                    return ValueResult<PluginManifest>.FromError(tileAssetError);

                tileAsset = validatedTileAsset;
            }

            if (!TryReadHexColor(root, "backgroundColor", out var backgroundColor, out var bgColorError))
                return ValueResult<PluginManifest>.FromError(bgColorError);
            if (!TryReadHexColor(root, "fontColor", out var fontColor, out var fontColorError))
                return ValueResult<PluginManifest>.FromError(fontColorError);

            bool workInProgress = false;
            if (root.TryGetProperty("workInProgress", out var wipElement)
                && wipElement.ValueKind != JsonValueKind.Null)
            {
                if (wipElement.ValueKind != JsonValueKind.True
                    && wipElement.ValueKind != JsonValueKind.False)
                {
                    return ValueResult<PluginManifest>.FromError(
                        "plugin.json 'workInProgress' must be a boolean.");
                }

                workInProgress = wipElement.GetBoolean();
            }

            var kind = PluginKind.Game;
            if (root.TryGetProperty("kind", out var kindElement)
                && kindElement.ValueKind != JsonValueKind.Null)
            {
                if (kindElement.ValueKind != JsonValueKind.String)
                {
                    return ValueResult<PluginManifest>.FromError(
                        "plugin.json 'kind' must be a string.");
                }

                var rawKind = kindElement.GetString()!;
                if (!KindByName.TryGetValue(rawKind, out kind))
                {
                    return ValueResult<PluginManifest>.FromError(
                        $"plugin.json declares unknown kind [{rawKind}]. Known kinds: " +
                        string.Join(", ", KindByName.Keys) + ".");
                }
            }

            var exportedContracts = Array.Empty<string>();
            if (root.TryGetProperty("exportedContracts", out var exportedElement)
                && exportedElement.ValueKind != JsonValueKind.Null)
            {
                if (exportedElement.ValueKind != JsonValueKind.Array)
                {
                    return ValueResult<PluginManifest>.FromError(
                        "plugin.json 'exportedContracts' must be an array of strings.");
                }

                var collected = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in exportedElement.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.String)
                    {
                        return ValueResult<PluginManifest>.FromError(
                            "plugin.json 'exportedContracts' entries must be strings.");
                    }

                    var raw = entry.GetString();
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        return ValueResult<PluginManifest>.FromError(
                            "plugin.json 'exportedContracts' entries must not be empty or whitespace.");
                    }

                    if (!ExportedContractNamePattern().IsMatch(raw))
                    {
                        return ValueResult<PluginManifest>.FromError(
                            $"plugin.json 'exportedContracts' entry [{raw}] must match ^[A-Za-z0-9._-]+$.");
                    }

                    if (!seen.Add(raw))
                    {
                        return ValueResult<PluginManifest>.FromError(
                            $"plugin.json 'exportedContracts' entry [{raw}] is listed more than once.");
                    }

                    collected.Add(raw);
                }

                exportedContracts = collected.ToArray();
            }

            // Cross-field rules:
            //   - exportedContracts is library-only. Games declaring contracts is almost
            //     always an authoring mistake (the contract type should live in a sibling
            //     library plugin), so reject loudly rather than silently dropping the list.
            //   - libraries must use strict Major.Minor.Patch SemVer. The loader's
            //     coexistence policy partitions libraries by (entryAssembly, Major, Minor)
            //     and picks the highest Patch; a 2-component or 4-component version makes
            //     that policy ambiguous.
            if (kind == PluginKind.Game && exportedContracts.Length > 0)
            {
                return ValueResult<PluginManifest>.FromError(
                    "plugin.json declares 'exportedContracts' but kind is 'game'. " +
                    "Only library plugins may export contracts; move the contract types " +
                    "into a sibling library plugin or set kind to 'library'.");
            }

            if (kind == PluginKind.Library)
            {
                // System.Version yields Build = -1 when the third component is absent and
                // Revision = -1 when the fourth is absent. Require Build >= 0 (patch
                // present) and Revision <= 0 (no fourth component, or explicitly zero).
                if (version.Build < 0 || version.Revision > 0)
                {
                    return ValueResult<PluginManifest>.FromError(
                        $"plugin.json version [{versionString}] for a library plugin must use strict " +
                        "Major.Minor.Patch SemVer (e.g. '1.2.3'); two-component or four-component-with-revision " +
                        "versions are not allowed because the loader's coexistence policy depends on " +
                        "Major.Minor grouping and Patch dedup.");
                }
            }

            var manifest = new PluginManifest(
                Name: name,
                Description: description,
                RouteIdentifier: routeIdentifier,
                Version: version,
                EntryAssembly: entryAssembly,
                Capabilities: capabilities,
                TileAsset: tileAsset,
                WorkInProgress: workInProgress,
                Kind: kind,
                ExportedContracts: exportedContracts,
                BackgroundColor: backgroundColor,
                FontColor: fontColor);

            return ValueResult<PluginManifest>.FromValue(manifest);
        }
    }

    /// <summary>
    /// Validates a manifest-declared <c>tileAsset</c> path. The path is a
    /// plugin-wwwroot-relative reference that the host turns into a
    /// <c>_content/{EntryAssembly}/{tileAsset}</c> URL — it never reaches a
    /// filesystem call from plugin code, but rejecting absolute paths, parent
    /// traversal, and backslashes here makes authoring mistakes loud at
    /// manifest-load time and keeps the URL well-formed across platforms.
    /// </summary>
    private static bool TryValidateTileAsset(string raw, out string normalized, out string error)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "plugin.json 'tileAsset' is empty or whitespace.";
            return false;
        }

        if (raw.Contains('\\'))
        {
            error = $"plugin.json 'tileAsset' [{raw}] must use forward slashes, not backslashes.";
            return false;
        }

        if (Path.IsPathRooted(raw))
        {
            error = $"plugin.json 'tileAsset' [{raw}] must be a relative path.";
            return false;
        }

        foreach (var segment in raw.Split('/'))
        {
            if (segment == "..")
            {
                error = $"plugin.json 'tileAsset' [{raw}] must not contain '..' segments.";
                return false;
            }
        }

        normalized = raw;
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Reads an optional hex-color manifest field. Absent or JSON <c>null</c> →
    /// <paramref name="color"/> is <c>null</c> and the read succeeds. A present
    /// value must be a non-string-rejecting, hex CSS color (<c>#rgb</c>,
    /// <c>#rgba</c>, <c>#rrggbb</c>, or <c>#rrggbbaa</c>); anything else fails the
    /// parse so authoring mistakes surface at manifest-load time.
    /// </summary>
    private static bool TryReadHexColor(JsonElement root, string propertyName, out string? color, out string error)
    {
        color = null;
        error = string.Empty;

        if (!root.TryGetProperty(propertyName, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            error = $"plugin.json '{propertyName}' must be a string.";
            return false;
        }

        var raw = element.GetString()!;
        if (!HexColorPattern().IsMatch(raw))
        {
            error = $"plugin.json '{propertyName}' [{raw}] must be a hex color like '#06080f' or '#fff'.";
            return false;
        }

        color = raw;
        return true;
    }

    private enum StringFieldStatus
    {
        Present,
        Missing,
        EmptyOrWhitespace,
    }

    private static StringFieldStatus ReadString(JsonElement root, string propertyName, out string value)
    {
        if (!root.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            value = string.Empty;
            return StringFieldStatus.Missing;
        }

        var s = element.GetString();
        if (string.IsNullOrWhiteSpace(s))
        {
            value = string.Empty;
            return StringFieldStatus.EmptyOrWhitespace;
        }

        value = s;
        return StringFieldStatus.Present;
    }

    private static bool TryRequireString(JsonElement root, string propertyName, out string value, out string error)
    {
        var status = ReadString(root, propertyName, out value);
        error = status switch
        {
            StringFieldStatus.Present => string.Empty,
            StringFieldStatus.Missing => $"plugin.json is missing required string '{propertyName}'.",
            StringFieldStatus.EmptyOrWhitespace => $"plugin.json required string '{propertyName}' is empty or whitespace.",
            _ => $"plugin.json required string '{propertyName}' could not be read.",
        };
        return status == StringFieldStatus.Present;
    }
}
