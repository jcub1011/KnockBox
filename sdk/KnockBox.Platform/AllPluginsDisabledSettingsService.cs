using KnockBox.Services.Logic.Admin;

namespace KnockBox.Platform
{
    /// <summary>
    /// Default <see cref="IAdminSettingsService"/> that treats all optional
    /// features as disabled. Registered via <c>TryAddSingleton</c> so the 
    /// production host can override.
    /// </summary>
    internal sealed class AllPluginsDisabledSettingsService : IAdminSettingsService
    {
        public bool GetEnableThirdPartyPlugins() => false;

        public ValueTask SetEnableThirdPartyPluginsAsync(bool enabled) 
            => ValueTask.CompletedTask;
    }
}
