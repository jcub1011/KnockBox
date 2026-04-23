using KnockBox.Core.Plugins;
using KnockBox.Platform.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Pins the capability-gating contract on <see cref="DefaultPluginContext"/>:
/// accessing <see cref="IPluginContext.Configuration"/> or <see cref="IPluginContext.Storage"/>
/// without the corresponding declaration in the plugin's manifest throws
/// <see cref="PluginCapabilityNotGrantedException"/>, and the exception carries
/// the offending plugin's route identifier and capability.
/// </summary>
[TestClass]
public sealed class DefaultPluginContextTests
{
    private const string TestRoute = "fixture-plugin";

    private static IPluginManifest Manifest(params PluginCapability[] caps) => new PluginManifest(
        Name: "Fixture",
        Description: "Fixture manifest.",
        RouteIdentifier: TestRoute,
        Version: new Version(1, 0, 0),
        EntryAssembly: "Fixture.Assembly",
        Capabilities: new HashSet<PluginCapability>(caps));

    private static IConfiguration EmptyConfiguration() =>
        new ConfigurationBuilder().Build();

    private static DefaultPluginContext NewContext(IPluginManifest manifest) => new(
        manifest: manifest,
        logger: NullLogger.Instance,
        configuration: EmptyConfiguration(),
        storage: Mock.Of<IPluginStorage>());

    // ─── No capabilities ────────────────────────────────────────────────────

    [TestMethod]
    public void NoCapabilities_ConfigurationThrowsWithConfigCapability()
    {
        var ctx = NewContext(Manifest());

        var ex = Assert.Throws<PluginCapabilityNotGrantedException>(() => _ = ctx.Configuration);

        Assert.AreEqual(TestRoute, ex.RouteIdentifier);
        Assert.AreEqual(PluginCapability.Config, ex.Capability);
        StringAssert.Contains(ex.Message, TestRoute);
    }

    [TestMethod]
    public void NoCapabilities_StorageThrowsWithStorageCapability()
    {
        var ctx = NewContext(Manifest());

        var ex = Assert.Throws<PluginCapabilityNotGrantedException>(() => _ = ctx.Storage);

        Assert.AreEqual(TestRoute, ex.RouteIdentifier);
        Assert.AreEqual(PluginCapability.Storage, ex.Capability);
        StringAssert.Contains(ex.Message, TestRoute);
    }

    [TestMethod]
    public void NoCapabilities_LoggerAndManifestAreAlwaysAccessible()
    {
        var ctx = NewContext(Manifest());

        Assert.IsNotNull(ctx.Logger);
        Assert.AreEqual(TestRoute, ctx.Manifest.RouteIdentifier);
    }

    // ─── Config only ────────────────────────────────────────────────────────

    [TestMethod]
    public void ConfigCapabilityOnly_ConfigurationReturnsInjectedInstance()
    {
        var config = EmptyConfiguration();
        var ctx = new DefaultPluginContext(
            manifest: Manifest(PluginCapability.Config),
            logger: NullLogger.Instance,
            configuration: config,
            storage: Mock.Of<IPluginStorage>());

        Assert.AreSame(config, ctx.Configuration);
    }

    [TestMethod]
    public void ConfigCapabilityOnly_StorageThrows()
    {
        var ctx = NewContext(Manifest(PluginCapability.Config));

        var ex = Assert.Throws<PluginCapabilityNotGrantedException>(() => _ = ctx.Storage);
        Assert.AreEqual(PluginCapability.Storage, ex.Capability);
    }

    // ─── Storage only ───────────────────────────────────────────────────────

    [TestMethod]
    public void StorageCapabilityOnly_StorageReturnsInjectedInstance()
    {
        var storage = Mock.Of<IPluginStorage>();
        var ctx = new DefaultPluginContext(
            manifest: Manifest(PluginCapability.Storage),
            logger: NullLogger.Instance,
            configuration: EmptyConfiguration(),
            storage: storage);

        Assert.AreSame(storage, ctx.Storage);
    }

    [TestMethod]
    public void StorageCapabilityOnly_ConfigurationThrows()
    {
        var ctx = NewContext(Manifest(PluginCapability.Storage));

        var ex = Assert.Throws<PluginCapabilityNotGrantedException>(() => _ = ctx.Configuration);
        Assert.AreEqual(PluginCapability.Config, ex.Capability);
    }

    // ─── All capabilities ───────────────────────────────────────────────────

    [TestMethod]
    public void AllCapabilities_BothConfigurationAndStorageAreAccessible()
    {
        var config = EmptyConfiguration();
        var storage = Mock.Of<IPluginStorage>();
        var ctx = new DefaultPluginContext(
            manifest: Manifest(PluginCapability.Config, PluginCapability.Storage),
            logger: NullLogger.Instance,
            configuration: config,
            storage: storage);

        Assert.AreSame(config, ctx.Configuration);
        Assert.AreSame(storage, ctx.Storage);
    }
}
