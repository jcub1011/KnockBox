using KnockBox.Core.Plugins;

namespace KnockBox.CoreTests.Unit.Plugins;

/// <summary>
/// Unit coverage for <see cref="PluginLoader.ManifestsAgree"/>. Pins the
/// per-field disagreement hints the loader surfaces when the on-disk
/// <c>plugin.json</c> contradicts the <see cref="IGameModule.Manifest"/> the
/// loaded assembly reports — integration-level coverage is in
/// <see cref="PluginLoaderTests"/>.
/// </summary>
[TestClass]
public sealed class ManifestsAgreeTests
{
    private static PluginManifest Baseline() => new(
        Name: "Baseline",
        Description: "Baseline plugin.",
        RouteIdentifier: "baseline",
        Version: new Version(1, 2, 3),
        EntryAssembly: "Baseline.Assembly",
        Capabilities: new HashSet<PluginCapability> { PluginCapability.Config });

    [TestMethod]
    public void ManifestsAgree_AllFieldsEqual_ReturnsTrueWithEmptyDisagreement()
    {
        var left = Baseline();
        var right = Baseline();

        var agree = PluginLoader.ManifestsAgree(left, right, out var disagreement);

        Assert.IsTrue(agree);
        Assert.AreEqual(string.Empty, disagreement);
    }

    [TestMethod]
    public void ManifestsAgree_CapabilitiesSameSetDifferentOrder_ReturnsTrue()
    {
        var onDisk = Baseline() with
        {
            Capabilities = new HashSet<PluginCapability> { PluginCapability.Config, PluginCapability.Storage },
        };
        var fromModule = Baseline() with
        {
            Capabilities = new HashSet<PluginCapability> { PluginCapability.Storage, PluginCapability.Config },
        };

        var agree = PluginLoader.ManifestsAgree(onDisk, fromModule, out var disagreement);

        Assert.IsTrue(agree);
        Assert.AreEqual(string.Empty, disagreement);
    }

    [TestMethod]
    [DataRow("Name")]
    [DataRow("Description")]
    [DataRow("RouteIdentifier")]
    [DataRow("EntryAssembly")]
    [DataRow("Version")]
    [DataRow("Capabilities")]
    [DataRow("ClientAssembly")]
    [DataRow("ClientContracts")]
    public void ManifestsAgree_SingleFieldDiffers_ReturnsFalseWithFieldInDisagreement(string field)
    {
        var onDisk = Baseline();
        var fromModule = MutateField(onDisk, field);

        var agree = PluginLoader.ManifestsAgree(onDisk, fromModule, out var disagreement);

        Assert.IsFalse(agree);
        StringAssert.Contains(disagreement, field);
    }

    [TestMethod]
    public void ManifestsAgree_ClientAssetsDiffer_StillAgrees()
    {
        // The embedded plugin.json carries no hashes; the on-disk one does. The
        // cross-check must ignore ClientAssets so this intentional asymmetry does
        // not reject an otherwise-matching plugin.
        var onDisk = Baseline() with
        {
            ClientAssets = new[] { new ClientAssetEntry("Baseline.Client", new string('a', 64)) },
        };
        var fromModule = Baseline(); // no hashes

        var agree = PluginLoader.ManifestsAgree(onDisk, fromModule, out var disagreement);

        Assert.IsTrue(agree, disagreement);
    }

    /// <summary>
    /// Returns a copy of <paramref name="source"/> with one field replaced so the
    /// two manifests disagree only on <paramref name="fieldName"/>.
    /// </summary>
    private static PluginManifest MutateField(PluginManifest source, string fieldName) => fieldName switch
    {
        "Name"            => source with { Name = "Other" },
        "Description"     => source with { Description = "Other." },
        "RouteIdentifier" => source with { RouteIdentifier = "other" },
        "EntryAssembly"   => source with { EntryAssembly = "Other.Assembly" },
        "Version"         => source with { Version = new Version(9, 9, 9) },
        "Capabilities"    => source with
        {
            Capabilities = new HashSet<PluginCapability> { PluginCapability.Storage },
        },
        "ClientAssembly"  => source with { ClientAssembly = "Other.Client" },
        "ClientContracts" => source with { ClientContracts = new[] { "Other.Contracts" } },
        _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, "Unknown field."),
    };
}
