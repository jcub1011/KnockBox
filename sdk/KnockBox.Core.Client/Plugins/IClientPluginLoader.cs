namespace KnockBox.Core.Client.Plugins;

/// <summary>
/// Pulls a game's runtime UI assembly into the browser on room entry and
/// resolves its root component. Hides the two load paths described in the
/// migration plan: build-time-declared first-party assemblies (future
/// <c>LazyAssemblyLoader</c> path) and runtime-unknown third-party assemblies
/// (the <c>LoadFromStream</c> path this spike exercises).
/// </summary>
public interface IClientPluginLoader
{
    /// <summary>
    /// Fetches the client manifest for <paramref name="routeIdentifier"/>,
    /// downloads + integrity-verifies the entry assembly, loads it into the
    /// (single) default load context, and resolves its <c>GameRoot</c> component.
    /// </summary>
    Task<GameRootLoadResult> LoadGameRootAsync(string routeIdentifier, CancellationToken ct = default);
}
