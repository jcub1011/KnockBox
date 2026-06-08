using Microsoft.AspNetCore.Components;

namespace KnockBox.Core.Client.Plugins;

/// <summary>
/// Outcome of <see cref="IClientPluginLoader.LoadGameRootAsync"/>. On success
/// <see cref="RootComponentType"/> is a resolved <see cref="IComponent"/> type
/// ready to hand to <c>DynamicComponent</c>; on failure <see cref="Error"/>
/// explains why (download, integrity, or resolution failure).
/// </summary>
public sealed record GameRootLoadResult(bool Ok, Type? RootComponentType, string? Error)
{
    public static GameRootLoadResult Success(Type rootType) => new(true, rootType, null);
    public static GameRootLoadResult Failure(string error) => new(false, null, error);
}
