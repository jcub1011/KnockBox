using System.Reflection;

namespace KnockBox.Core.Plugins
{
    /// <summary>
    /// Holds the assemblies of dynamically discovered game plugins so that
    /// Blazor's Router can scan them for @page components.
    /// </summary>
    public sealed class GamePluginAssemblies(IEnumerable<Assembly> assemblies)
    {
        public IReadOnlyList<Assembly> Assemblies { get; } = [.. assemblies];
    }
}
