using System.Reflection;
using System.Runtime.Loader;

namespace KnockBox.Core.Plugins
{
    /// <summary>
    /// Per-plugin <see cref="AssemblyLoadContext"/> that resolves transitive dependencies from
    /// the plugin's own folder via <see cref="AssemblyDependencyResolver"/>, while deferring
    /// shared-contract assemblies (anything already loaded by the host) back to the default
    /// ALC so type identity is preserved across the host/plugin boundary.
    /// </summary>
    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly Func<AssemblyName, bool> _isSharedContract;

        public PluginLoadContext(string pluginPath, Func<AssemblyName, bool> isSharedContract)
            : base(name: Path.GetFileNameWithoutExtension(pluginPath), isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
            _isSharedContract = isSharedContract;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Returning null falls back to the default ALC, which is how we keep type
            // identity for shared contracts (IGameModule, AbstractGameEngine, etc.).
            if (_isSharedContract(assemblyName))
                return null;

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
