using System.Text;
using KnockBox.Core.Plugins;
using KnockBox.CoreTests.EmbeddedManifestFixture;

namespace KnockBox.CoreTests.Unit.Plugins;

/// <summary>
/// Parser coverage for <see cref="PluginManifest"/>. The loader integration path
/// is covered by <see cref="PluginLoaderTests"/>; these tests pin the JSON-shape
/// contract one branch at a time so a regression in validation surfaces as a
/// specific failed assertion instead of a cascading integration break.
/// </summary>
[TestClass]
public sealed class PluginManifestTests
{
    private static Stream StreamFor(string json) =>
        new MemoryStream(Encoding.UTF8.GetBytes(json));

    private const string ValidManifest = """
        {
            "schemaVersion": 1,
            "name": "Fixture",
            "description": "A fixture manifest.",
            "routeIdentifier": "fixture",
            "version": "1.0.0",
            "entryAssembly": "Fixture.Assembly",
            "capabilities": []
        }
        """;

    // ─── TryParse — happy path ──────────────────────────────────────────────

    [TestMethod]
    public void TryParse_ValidMinimalManifest_Succeeds()
    {
        using var stream = StreamFor(ValidManifest);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.AreEqual("Fixture", manifest.Name);
        Assert.AreEqual("A fixture manifest.", manifest.Description);
        Assert.AreEqual("fixture", manifest.RouteIdentifier);
        Assert.AreEqual(new Version(1, 0, 0), manifest.Version);
        Assert.AreEqual("Fixture.Assembly", manifest.EntryAssembly);
        Assert.AreEqual(0, manifest.Capabilities.Count);
    }

    [TestMethod]
    public void TryParse_ManifestWithoutCapabilitiesProperty_SucceedsWithEmptyCapabilities()
    {
        using var stream = StreamFor("""
            {
                "schemaVersion": 1,
                "name": "Fixture",
                "description": "d",
                "routeIdentifier": "fixture",
                "version": "1.0.0",
                "entryAssembly": "Fixture.Assembly"
            }
            """);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.AreEqual(0, manifest.Capabilities.Count);
    }

    [TestMethod]
    public void TryParse_CapabilitiesConfigOnly_SucceedsWithConfigFlag()
    {
        using var stream = StreamFor(ValidManifestWithCapabilities("""["config"]"""));

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.IsTrue(manifest.Capabilities.Contains(PluginCapability.Config));
        Assert.IsFalse(manifest.Capabilities.Contains(PluginCapability.Storage));
    }

    [TestMethod]
    public void TryParse_CapabilitiesMixedCase_IsCaseInsensitive()
    {
        using var stream = StreamFor(ValidManifestWithCapabilities("""["storage", "CONFIG"]"""));

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.IsTrue(manifest.Capabilities.Contains(PluginCapability.Config));
        Assert.IsTrue(manifest.Capabilities.Contains(PluginCapability.Storage));
        Assert.AreEqual(2, manifest.Capabilities.Count);
    }

    // ─── TryParse — capabilities errors ─────────────────────────────────────

    [TestMethod]
    public void TryParse_UnknownCapability_FailsListingKnownCapabilities()
    {
        using var stream = StreamFor(ValidManifestWithCapabilities("""["storage", "gpu"]"""));

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "gpu", "config", "storage");
    }

    [TestMethod]
    public void TryParse_NonStringCapabilityEntry_Fails()
    {
        using var stream = StreamFor(ValidManifestWithCapabilities("""["storage", 1]"""));

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "capabilities");
    }

    [TestMethod]
    public void TryParse_CapabilitiesNotArray_Fails()
    {
        using var stream = StreamFor(ValidManifestWithCapabilitiesRaw("""{}"""));

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "capabilities", "array");
    }

    // ─── TryParse — tileAsset ───────────────────────────────────────────────

    [TestMethod]
    public void TryParse_ManifestWithoutTileAsset_SucceedsWithNullTileAsset()
    {
        using var stream = StreamFor(ValidManifest);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.IsNull(manifest.TileAsset);
    }

    [TestMethod]
    [DataRow("tile.svg")]
    [DataRow("assets/tile.svg")]
    [DataRow("a/b/c.svg")]
    public void TryParse_ValidTileAsset_RoundTripsPath(string path)
    {
        var json = AddField(ValidManifest, "tileAsset", $"\"{path}\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.AreEqual(path, manifest.TileAsset);
    }

    [TestMethod]
    public void TryParse_TileAssetExplicitlyNull_SucceedsWithNullTileAsset()
    {
        var json = AddField(ValidManifest, "tileAsset", "null");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.IsNull(manifest.TileAsset);
    }

    [TestMethod]
    public void TryParse_TileAssetNotString_Fails()
    {
        var json = AddField(ValidManifest, "tileAsset", "42");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "tileAsset", "string");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void TryParse_EmptyOrWhitespaceTileAsset_Fails(string value)
    {
        var json = AddField(ValidManifest, "tileAsset", $"\"{value}\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "tileAsset");
    }

    [TestMethod]
    [DataRow("/abs/tile.svg")]
    [DataRow("C:/Windows/tile.svg")]
    public void TryParse_AbsoluteTileAssetPath_Fails(string path)
    {
        var json = AddField(ValidManifest, "tileAsset", $"\"{path}\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "tileAsset", "relative");
    }

    [TestMethod]
    [DataRow("..\\tile.svg")]
    [DataRow("assets\\tile.svg")]
    public void TryParse_TileAssetWithBackslash_Fails(string path)
    {
        var json = AddField(ValidManifest, "tileAsset", $"\"{path.Replace("\\", "\\\\")}\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "tileAsset", "forward");
    }

    [TestMethod]
    [DataRow("../tile.svg")]
    [DataRow("a/../b.svg")]
    [DataRow("..")]
    public void TryParse_TileAssetWithParentTraversal_Fails(string path)
    {
        var json = AddField(ValidManifest, "tileAsset", $"\"{path}\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "tileAsset", "..");
    }

    // ─── TryParse — schemaVersion errors ────────────────────────────────────

    [TestMethod]
    public void TryParse_MissingSchemaVersion_Fails()
    {
        using var stream = StreamFor("""
            {
                "name": "x", "description": "x", "routeIdentifier": "x",
                "version": "1.0.0", "entryAssembly": "x"
            }
            """);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "schemaVersion");
    }

    [TestMethod]
    public void TryParse_SchemaVersionAsString_Fails()
    {
        using var stream = StreamFor("""
            {
                "schemaVersion": "1",
                "name": "x", "description": "x", "routeIdentifier": "x",
                "version": "1.0.0", "entryAssembly": "x"
            }
            """);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "schemaVersion");
    }

    [TestMethod]
    public void TryParse_UnsupportedSchemaVersion_FailsMentioningSupportedVersion()
    {
        using var stream = StreamFor("""
            {
                "schemaVersion": 99,
                "name": "x", "description": "x", "routeIdentifier": "x",
                "version": "1.0.0", "entryAssembly": "x"
            }
            """);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "99", PluginManifest.SupportedSchemaVersion.ToString());
    }

    // ─── TryParse — required field errors ───────────────────────────────────

    [TestMethod]
    [DataRow("name")]
    [DataRow("description")]
    [DataRow("routeIdentifier")]
    [DataRow("version")]
    [DataRow("entryAssembly")]
    public void TryParse_MissingRequiredString_Fails(string field)
    {
        var json = RemoveField(ValidManifest, field);
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, field);
    }

    [TestMethod]
    [DataRow("name")]
    [DataRow("description")]
    [DataRow("routeIdentifier")]
    [DataRow("version")]
    [DataRow("entryAssembly")]
    public void TryParse_WhitespaceRequiredString_FailsAsEmpty(string field)
    {
        var json = ReplaceFieldValue(ValidManifest, field, "\"   \"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, field);
        Assert.IsTrue(result.TryGetFailure(out var error));
        Assert.IsTrue(
            error.PublicMessage.Contains("empty", StringComparison.OrdinalIgnoreCase)
                || error.PublicMessage.Contains("whitespace", StringComparison.OrdinalIgnoreCase),
            $"Expected whitespace-only field '{field}' to report 'empty' or 'whitespace', got: {error.PublicMessage}");
        Assert.IsFalse(
            error.PublicMessage.Contains("missing", StringComparison.OrdinalIgnoreCase),
            $"Whitespace-only field '{field}' must not be reported as 'missing'. Got: {error.PublicMessage}");
    }

    [TestMethod]
    [DataRow("name")]
    [DataRow("description")]
    [DataRow("routeIdentifier")]
    [DataRow("version")]
    [DataRow("entryAssembly")]
    public void TryParse_EmptyRequiredString_FailsAsEmpty(string field)
    {
        var json = ReplaceFieldValue(ValidManifest, field, "\"\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, field);
        Assert.IsTrue(result.TryGetFailure(out var error));
        Assert.IsTrue(
            error.PublicMessage.Contains("empty", StringComparison.OrdinalIgnoreCase)
                || error.PublicMessage.Contains("whitespace", StringComparison.OrdinalIgnoreCase),
            $"Expected empty field '{field}' to report 'empty' or 'whitespace', got: {error.PublicMessage}");
    }

    // ─── TryParse — format errors ───────────────────────────────────────────

    [TestMethod]
    [DataRow("Fixture")]   // uppercase
    [DataRow("fix_ture")]  // underscore
    [DataRow("fix ture")]  // space
    public void TryParse_InvalidRouteIdentifierShape_Fails(string badRoute)
    {
        var json = ReplaceFieldValue(ValidManifest, "routeIdentifier", $"\"{badRoute}\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "routeIdentifier", "^[a-z0-9-]+$");
    }

    [TestMethod]
    public void TryParse_VersionNotParseable_Fails()
    {
        var json = ReplaceFieldValue(ValidManifest, "version", "\"abc\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "version");
    }

    // ─── TryParse — JSON shape errors ───────────────────────────────────────

    [TestMethod]
    public void TryParse_RootIsArray_Fails()
    {
        using var stream = StreamFor("[]");

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "object");
    }

    [TestMethod]
    public void TryParse_MalformedJson_Fails()
    {
        using var stream = StreamFor("{ this is not valid json");

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "Malformed");
    }

    // ─── TryReadFromFile ────────────────────────────────────────────────────

    [TestMethod]
    public void TryReadFromFile_NonexistentPath_Fails()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");

        var result = PluginManifest.TryReadFromFile(path);

        Assert.IsFalse(result.TryGetSuccess(out _));
    }

    [TestMethod]
    public void TryReadFromFile_PathIsDirectory_Fails()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var result = PluginManifest.TryReadFromFile(dir);

            Assert.IsFalse(result.TryGetSuccess(out _));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void TryReadFromFile_ValidFile_Succeeds()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, ValidManifest);
        try
        {
            var result = PluginManifest.TryReadFromFile(path);

            Assert.IsTrue(result.TryGetSuccess(out var manifest));
            Assert.AreEqual("fixture", manifest.RouteIdentifier);
        }
        finally { File.Delete(path); }
    }

    // ─── FromEmbeddedResource / FromEmbeddedResourceOrThrow ─────────────────

    [TestMethod]
    public void FromEmbeddedResource_AssemblyWithNoManifestResource_Fails()
    {
        // KnockBox.Core itself doesn't embed plugin.json, so it's a convenient
        // "no embedded resource" stand-in.
        var assembly = typeof(PluginManifest).Assembly;

        var result = PluginManifest.FromEmbeddedResource(assembly);

        Assert.IsFalse(result.TryGetSuccess(out _));
        Assert.IsTrue(result.TryGetFailure(out var error));
        StringAssert.Contains(error.PublicMessage, "plugin.json");
    }

    [TestMethod]
    public void FromEmbeddedResource_FixtureAssemblyWithValidManifest_Succeeds()
    {
        var assembly = typeof(FixtureMarker).Assembly;

        var result = PluginManifest.FromEmbeddedResource(assembly);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.AreEqual("embedded-fixture", manifest.RouteIdentifier);
        Assert.AreEqual(new Version(1, 2, 3), manifest.Version);
        Assert.IsTrue(manifest.Capabilities.Contains(PluginCapability.Config));
        Assert.IsTrue(manifest.Capabilities.Contains(PluginCapability.Storage));
    }

    [TestMethod]
    public void FromEmbeddedResourceOrThrow_AssemblyWithoutManifest_ThrowsWithAssemblyName()
    {
        var assembly = typeof(PluginManifest).Assembly;
        var expectedAssemblyName = assembly.GetName().Name!;

        var ex = Assert.Throws<InvalidOperationException>(
            () => PluginManifest.FromEmbeddedResourceOrThrow(assembly));

        StringAssert.Contains(ex.Message, expectedAssemblyName);
    }

    [TestMethod]
    public void FromEmbeddedResourceOrThrow_FixtureAssembly_ReturnsManifest()
    {
        var manifest = PluginManifest.FromEmbeddedResourceOrThrow(typeof(FixtureMarker).Assembly);

        Assert.AreEqual("embedded-fixture", manifest.RouteIdentifier);
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private static string ValidManifestWithCapabilities(string capabilitiesJsonArray) =>
        ValidManifestWithCapabilitiesRaw(capabilitiesJsonArray);

    private static string ValidManifestWithCapabilitiesRaw(string rawCapabilitiesLiteral) =>
        $$"""
            {
                "schemaVersion": 1,
                "name": "Fixture",
                "description": "A fixture manifest.",
                "routeIdentifier": "fixture",
                "version": "1.0.0",
                "entryAssembly": "Fixture.Assembly",
                "capabilities": {{rawCapabilitiesLiteral}}
            }
            """;

    /// <summary>
    /// Removes a top-level JSON property by name from a manifest string. Crude but
    /// sufficient — we only use well-formed fixtures here.
    /// </summary>
    private static string RemoveField(string json, string fieldName)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var sb = new StringBuilder();
        sb.Append('{');
        var first = true;
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Name == fieldName)
                continue;

            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(property.Name).Append("\":").Append(property.Value.GetRawText());
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Appends a new top-level property to a manifest JSON string. Used by the
    /// tileAsset tests so the base <see cref="ValidManifest"/> stays untouched.
    /// </summary>
    private static string AddField(string json, string fieldName, string rawJsonValue)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var sb = new StringBuilder();
        sb.Append('{');
        var first = true;
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(property.Name).Append("\":").Append(property.Value.GetRawText());
        }
        if (!first) sb.Append(',');
        sb.Append('"').Append(fieldName).Append("\":").Append(rawJsonValue);
        sb.Append('}');
        return sb.ToString();
    }

    private static string ReplaceFieldValue(string json, string fieldName, string replacementRawJsonValue)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var sb = new StringBuilder();
        sb.Append('{');
        var first = true;
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(property.Name).Append("\":");
            sb.Append(property.Name == fieldName ? replacementRawJsonValue : property.Value.GetRawText());
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static void AssertFailureContains<T>(
        KnockBox.Core.Primitives.Returns.ValueResult<T> result,
        params string[] expectedSubstrings)
    {
        Assert.IsFalse(result.TryGetSuccess(out _), "Expected failure but got success.");
        Assert.IsTrue(result.TryGetFailure(out var error));
        foreach (var substring in expectedSubstrings)
            StringAssert.Contains(error.PublicMessage, substring);
    }
}
