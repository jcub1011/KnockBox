using System.Reflection;

namespace KnockBox.Core.Plugins
{
    /// <summary>
    /// Holds the assemblies of dynamically discovered game plugins so that
    /// Blazor's Router can scan them for @page components.
    /// </summary>
    /// <remarks>
    /// Plugin assemblies are held as <b>strong references</b> for the lifetime
    /// of the host process. This is deliberate: plugin hot-reload / unload is
    /// not a supported scenario. If that ever changes, this type will need to
    /// switch to <c>WeakReference&lt;Assembly&gt;</c> and the <c>AdditionalAssemblies</c>
    /// binding in <c>MapRazorComponents</c> will need to tolerate collected
    /// entries.
    /// </remarks>
    public sealed class GamePluginAssemblies(IEnumerable<Assembly> assemblies)
    {
        /// <summary>
        /// The plugin assemblies to include in Blazor routing alongside the
        /// host's own assembly. Passed as <c>AdditionalAssemblies</c> to
        /// <c>Router</c> / <c>MapRazorComponents</c>.
        /// </summary>
        public IReadOnlyList<Assembly> Assemblies { get; } = [.. assemblies];
    }
}
