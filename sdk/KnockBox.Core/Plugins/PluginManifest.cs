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
    IReadOnlySet<PluginCapability> Capabilities) : IPluginManifest
{
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

    private static readonly FrozenDictionary<string, PluginCapability> CapabilityByName =
        new Dictionary<string, PluginCapability>(StringComparer.OrdinalIgnoreCase)
        {
            ["config"] = PluginCapability.Config,
            ["storage"] = PluginCapability.Storage,
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

            if (!TryGetString(root, "name", out var name))
                return ValueResult<PluginManifest>.FromError("plugin.json is missing required string 'name'.");
            if (!TryGetString(root, "description", out var description))
                return ValueResult<PluginManifest>.FromError("plugin.json is missing required string 'description'.");
            if (!TryGetString(root, "routeIdentifier", out var routeIdentifier))
                return ValueResult<PluginManifest>.FromError("plugin.json is missing required string 'routeIdentifier'.");
            if (!TryGetString(root, "version", out var versionString))
                return ValueResult<PluginManifest>.FromError("plugin.json is missing required string 'version'.");
            if (!TryGetString(root, "entryAssembly", out var entryAssembly))
                return ValueResult<PluginManifest>.FromError("plugin.json is missing required string 'entryAssembly'.");

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

            var manifest = new PluginManifest(
                Name: name,
                Description: description,
                RouteIdentifier: routeIdentifier,
                Version: version,
                EntryAssembly: entryAssembly,
                Capabilities: capabilities);

            return ValueResult<PluginManifest>.FromValue(manifest);
        }
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        if (root.TryGetProperty(propertyName, out var element)
            && element.ValueKind == JsonValueKind.String)
        {
            var s = element.GetString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                value = s;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}
