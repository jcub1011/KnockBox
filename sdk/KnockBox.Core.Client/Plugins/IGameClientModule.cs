using Microsoft.AspNetCore.Components;

namespace KnockBox.Core.Client.Plugins;

/// <summary>
/// Browser-side counterpart to the server <c>IGameModule</c>: declares a runtime-
/// loaded game UI assembly's route and its root component type. Formalizes the
/// convention <see cref="RuntimePluginLoader"/> resolves by name today
/// (<c>{RootNamespace}.GameRoot</c>); a client assembly may instead ship a public
/// parameterless implementation of this interface to declare its root explicitly.
/// </summary>
public interface IGameClientModule
{
    /// <summary>
    /// Route segment this UI serves, matching the server plugin's
    /// <c>RouteIdentifier</c> (e.g. <c>"hidden-agenda"</c>).
    /// </summary>
    string RouteIdentifier { get; }

    /// <summary>
    /// The root component the client renders for this game via
    /// <c>DynamicComponent</c>. Must implement <see cref="IComponent"/>.
    /// </summary>
    Type GameRootComponentType { get; }
}
