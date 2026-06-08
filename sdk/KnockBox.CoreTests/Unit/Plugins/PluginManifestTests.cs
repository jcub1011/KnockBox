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

    // ─── TryParse — play-log theme colors ───────────────────────────────────

    [TestMethod]
    public void TryParse_NoColors_DefaultsToNull()
    {
        using var stream = StreamFor(ValidManifest);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.IsNull(manifest.BackgroundColor);
        Assert.IsNull(manifest.FontColor);
    }

    [TestMethod]
    public void TryParse_ValidHexColors_AreParsed()
    {
        using var stream = StreamFor("""
            {
                "schemaVersion": 1,
                "name": "Fixture",
                "description": "d",
                "routeIdentifier": "fixture",
                "version": "1.0.0",
                "entryAssembly": "Fixture.Assembly",
                "backgroundColor": "#06080f",
                "fontColor": "#fff"
            }
            """);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.AreEqual("#06080f", manifest.BackgroundColor);
        Assert.AreEqual("#fff", manifest.FontColor);
    }

    [TestMethod]
    public void TryParse_InvalidHexColor_Fails()
    {
        using var stream = StreamFor("""
            {
                "schemaVersion": 1,
                "name": "Fixture",
                "description": "d",
                "routeIdentifier": "fixture",
                "version": "1.0.0",
                "entryAssembly": "Fixture.Assembly",
                "backgroundColor": "navy"
            }
            """);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetFailure(out _), "A non-hex color must fail the parse.");
    }

    [TestMethod]
    public void TryParse_NonStringColor_Fails()
    {
        using var stream = StreamFor("""
            {
                "schemaVersion": 1,
                "name": "Fixture",
                "description": "d",
                "routeIdentifier": "fixture",
                "version": "1.0.0",
                "entryAssembly": "Fixture.Assembly",
                "fontColor": 1234
            }
            """);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetFailure(out _), "A non-string color must fail the parse.");
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

    // ─── TryParse — workInProgress ──────────────────────────────────────────

    [TestMethod]
    public void TryParse_ManifestWithoutWorkInProgress_DefaultsToFalse()
    {
        using var stream = StreamFor(ValidManifest);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.IsFalse(manifest.WorkInProgress);
    }

    [TestMethod]
    public void TryParse_WorkInProgressTrue_IsTrue()
    {
        var json = AddField(ValidManifest, "workInProgress", "true");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.IsTrue(manifest.WorkInProgress);
    }

    [TestMethod]
    public void TryParse_WorkInProgressFalse_IsFalse()
    {
        var json = AddField(ValidManifest, "workInProgress", "false");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.IsFalse(manifest.WorkInProgress);
    }

    [TestMethod]
    public void TryParse_WorkInProgressExplicitNull_DefaultsToFalse()
    {
        var json = AddField(ValidManifest, "workInProgress", "null");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.IsFalse(manifest.WorkInProgress);
    }

    [TestMethod]
    [DataRow("\"true\"")]
    [DataRow("1")]
    [DataRow("0")]
    [DataRow("[]")]
    public void TryParse_WorkInProgressNonBoolean_Fails(string rawValue)
    {
        var json = AddField(ValidManifest, "workInProgress", rawValue);
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "workInProgress", "boolean");
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

    // ─── TryParse — kind ────────────────────────────────────────────────────

    [TestMethod]
    public void TryParse_ManifestWithoutKind_DefaultsToGame()
    {
        using var stream = StreamFor(ValidManifest);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.AreEqual(PluginKind.Game, manifest.Kind);
    }

    [TestMethod]
    [DataRow("game")]
    [DataRow("Game")]
    [DataRow("GAME")]
    public void TryParse_KindGameCaseInsensitive_ParsesToGame(string kind)
    {
        var json = AddField(ValidManifest, "kind", $"\"{kind}\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.AreEqual(PluginKind.Game, manifest.Kind);
    }

    [TestMethod]
    [DataRow("library")]
    [DataRow("Library")]
    [DataRow("LIBRARY")]
    public void TryParse_KindLibraryCaseInsensitive_ParsesToLibrary(string kind)
    {
        var json = AddField(ValidManifest, "kind", $"\"{kind}\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.AreEqual(PluginKind.Library, manifest.Kind);
    }

    [TestMethod]
    public void TryParse_UnknownKind_FailsListingKnownKinds()
    {
        var json = AddField(ValidManifest, "kind", "\"plugin\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "plugin", "game", "library");
    }

    [TestMethod]
    public void TryParse_KindNotString_Fails()
    {
        var json = AddField(ValidManifest, "kind", "42");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "kind", "string");
    }

    [TestMethod]
    public void TryParse_KindExplicitlyNull_DefaultsToGame()
    {
        var json = AddField(ValidManifest, "kind", "null");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.AreEqual(PluginKind.Game, manifest.Kind);
    }

    // ─── TryParse — exportedContracts ───────────────────────────────────────

    [TestMethod]
    public void TryParse_ManifestWithoutExportedContracts_DefaultsToEmpty()
    {
        using var stream = StreamFor(ValidManifest);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.AreEqual(0, manifest.ExportedContracts.Count);
    }

    [TestMethod]
    public void TryParse_LibraryWithValidExportedContracts_RoundTrips()
    {
        var json = AddField(ValidManifest, "kind", "\"library\"");
        json = AddField(json, "exportedContracts", """["Foo.Contracts", "Bar-Baz_v2.Contracts"]""");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.AreEqual(2, manifest.ExportedContracts.Count);
        Assert.AreEqual("Foo.Contracts", manifest.ExportedContracts[0]);
        Assert.AreEqual("Bar-Baz_v2.Contracts", manifest.ExportedContracts[1]);
    }

    [TestMethod]
    public void TryParse_GameWithNonEmptyExportedContracts_FailsExplaining()
    {
        var json = AddField(ValidManifest, "exportedContracts", """["Foo.Contracts"]""");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "exportedContracts", "library");
    }

    [TestMethod]
    public void TryParse_ExportedContractsNotArray_Fails()
    {
        var json = AddField(ValidManifest, "kind", "\"library\"");
        json = AddField(json, "exportedContracts", """{"key": "value"}""");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "exportedContracts", "array");
    }

    [TestMethod]
    public void TryParse_ExportedContractsContainsNonString_Fails()
    {
        var json = AddField(ValidManifest, "kind", "\"library\"");
        json = AddField(json, "exportedContracts", """["Foo.Contracts", 42]""");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "exportedContracts", "string");
    }

    [TestMethod]
    public void TryParse_ExportedContractsContainsEmpty_Fails()
    {
        var json = AddField(ValidManifest, "kind", "\"library\"");
        json = AddField(json, "exportedContracts", """["", "Foo.Contracts"]""");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "exportedContracts");
    }

    [TestMethod]
    [DataRow("Foo Contracts")]
    [DataRow("Foo/Contracts")]
    [DataRow("Foo*Contracts")]
    public void TryParse_ExportedContractsInvalidCharacters_Fails(string badName)
    {
        var json = AddField(ValidManifest, "kind", "\"library\"");
        json = AddField(json, "exportedContracts", $"""["{badName}"]""");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "exportedContracts");
    }

    [TestMethod]
    public void TryParse_ExportedContractsContainsDuplicate_Fails()
    {
        var json = AddField(ValidManifest, "kind", "\"library\"");
        json = AddField(json, "exportedContracts", """["Foo.Contracts", "Foo.Contracts"]""");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "exportedContracts", "Foo.Contracts");
    }

    // ─── TryParse — library SemVer enforcement ──────────────────────────────

    [TestMethod]
    public void TryParse_LibraryWithMajorMinorPatch_Succeeds()
    {
        var json = AddField(ValidManifest, "kind", "\"library\"");
        json = ReplaceFieldValue(json, "version", "\"1.2.3\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.AreEqual(new Version(1, 2, 3), manifest.Version);
    }

    [TestMethod]
    public void TryParse_LibraryWithMajorMinorOnly_Fails()
    {
        var json = AddField(ValidManifest, "kind", "\"library\"");
        json = ReplaceFieldValue(json, "version", "\"1.2\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "version", "Major.Minor.Patch");
    }

    [TestMethod]
    public void TryParse_LibraryWithRevisionComponent_Fails()
    {
        var json = AddField(ValidManifest, "kind", "\"library\"");
        json = ReplaceFieldValue(json, "version", "\"1.2.3.4\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "version", "Major.Minor.Patch");
    }

    [TestMethod]
    public void TryParse_LibraryWithZeroRevision_Succeeds()
    {
        // System.Version("1.2.3.0") parses with Revision=0; that's not the
        // ambiguous case our policy is trying to reject (1.2.3.4 would be).
        var json = AddField(ValidManifest, "kind", "\"library\"");
        json = ReplaceFieldValue(json, "version", "\"1.2.3.0\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out _));
    }

    [TestMethod]
    public void TryParse_GameWithMajorMinorOnly_StillSucceeds()
    {
        // Game manifests don't have the strict SemVer requirement libraries do,
        // so the existing "1.0" / "1.2.3.4" formats still work.
        var json = ReplaceFieldValue(ValidManifest, "version", "\"1.0\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out _));
    }

    // ─── TryParse — client (browser UI) fields ─────────────────────────────

    [TestMethod]
    public void TryParse_ManifestWithoutClientFields_DefaultsToEmpty()
    {
        using var stream = StreamFor(ValidManifest);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.IsNull(manifest.ClientAssembly);
        Assert.AreEqual(0, manifest.ClientContracts.Count);
        Assert.AreEqual(0, manifest.ClientAssets.Count);
    }

    [TestMethod]
    public void TryParse_ValidClientFields_RoundTrip()
    {
        var json = AddField(ValidManifest, "clientAssembly", "\"Fixture.Client\"");
        json = AddField(json, "clientContracts", """["Fixture.Contracts"]""");
        json = AddField(json, "clientAssets", """
            [
                { "name": "Fixture.Client", "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" },
                { "name": "Fixture.Contracts", "sha256": "FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210" }
            ]
            """);
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        Assert.IsTrue(result.TryGetSuccess(out var manifest));
        Assert.AreEqual("Fixture.Client", manifest.ClientAssembly);
        CollectionAssert.AreEqual(new[] { "Fixture.Contracts" }, manifest.ClientContracts.ToArray());
        Assert.AreEqual(2, manifest.ClientAssets.Count);
        Assert.AreEqual("Fixture.Client", manifest.ClientAssets[0].Name);
        Assert.AreEqual(64, manifest.ClientAssets[0].Sha256.Length);
    }

    [TestMethod]
    public void TryParse_ClientAssemblyNotString_Fails()
    {
        var json = AddField(ValidManifest, "clientAssembly", "42");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "clientAssembly", "string");
    }

    [TestMethod]
    [DataRow("Bad Name")]
    [DataRow("Bad/Name")]
    public void TryParse_ClientAssemblyInvalidShape_Fails(string badName)
    {
        var json = AddField(ValidManifest, "clientAssembly", $"\"{badName}\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "clientAssembly");
    }

    [TestMethod]
    public void TryParse_ClientAssemblyWithoutMatchingAsset_Fails()
    {
        var json = AddField(ValidManifest, "clientAssembly", "\"Fixture.Client\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "clientAssembly", "clientAssets");
    }

    [TestMethod]
    public void TryParse_ClientContractsContainsDuplicate_Fails()
    {
        var json = AddField(ValidManifest, "clientContracts", """["Foo.Contracts", "Foo.Contracts"]""");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "clientContracts", "Foo.Contracts");
    }

    [TestMethod]
    public void TryParse_ClientContractsNotArray_Fails()
    {
        var json = AddField(ValidManifest, "clientContracts", "\"Foo.Contracts\"");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "clientContracts", "array");
    }

    [TestMethod]
    public void TryParse_ClientAssetsEntryNotObject_Fails()
    {
        var json = AddField(ValidManifest, "clientAssets", """["not-an-object"]""");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "clientAssets");
    }

    [TestMethod]
    public void TryParse_ClientAssetsMissingSha_Fails()
    {
        var json = AddField(ValidManifest, "clientAssets", """[ { "name": "Fixture.Client" } ]""");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "clientAssets", "sha256");
    }

    [TestMethod]
    [DataRow("0123")]                                                                   // too short
    [DataRow("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")]      // non-hex 'g'
    public void TryParse_ClientAssetsBadSha_Fails(string badSha)
    {
        var json = AddField(ValidManifest, "clientAssets",
            $$"""[ { "name": "Fixture.Client", "sha256": "{{badSha}}" } ]""");
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "clientAssets", "sha256");
    }

    [TestMethod]
    public void TryParse_ClientAssetsDuplicateName_Fails()
    {
        var sha = new string('a', 64);
        var json = AddField(ValidManifest, "clientAssets",
            $$"""
            [
                { "name": "Fixture.Client", "sha256": "{{sha}}" },
                { "name": "Fixture.Client", "sha256": "{{sha}}" }
            ]
            """);
        using var stream = StreamFor(json);

        var result = PluginManifest.TryParse(stream);

        AssertFailureContains(result, "clientAssets", "Fixture.Client");
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
