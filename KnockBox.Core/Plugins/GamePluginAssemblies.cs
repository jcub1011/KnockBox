using System.Reflection;

namespace KnockBox.Core.Plugins
{
    /// <summary>
    /// Holds the assemblies of dynamically discovered game plugins so that
    /// Blazor's Router can scan them for @page components.
    /// </summary>
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
