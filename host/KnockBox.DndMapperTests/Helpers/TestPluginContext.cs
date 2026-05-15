using KnockBox.Core.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.DndMapperTests.Helpers
{
    /// <summary>
    /// Minimal IPluginContext for unit tests — exposes the supplied
    /// <see cref="IPluginStorage"/> directly without going through the platform's
    /// capability-gated wrapper.
    /// </summary>
    internal sealed class TestPluginContext : IPluginContext
    {
        public TestPluginContext(IPluginStorage storage, IPluginManifest? manifest = null)
        {
            Storage = storage;
            Manifest = manifest ?? new TestPluginManifest();
        }

        public IPluginManifest Manifest { get; }
        public ILogger Logger { get; } = NullLogger.Instance;
        public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
        public IPluginStorage Storage { get; }

        private sealed class TestPluginManifest : IPluginManifest
        {
            public string Name => "DnD Mapper";
            public string Description => "Test";
            public string RouteIdentifier => "dnd-mapper";
            public Version Version { get; } = new(1, 0, 0);
            public string EntryAssembly => "KnockBox.DndMapper";
            public IReadOnlySet<PluginCapability> Capabilities { get; }
                = new HashSet<PluginCapability> { PluginCapability.Storage };
        }
    }
}
