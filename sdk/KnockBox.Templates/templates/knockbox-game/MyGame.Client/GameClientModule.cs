// -----------------------------------------------------------------------------
// Client UI entry-point declaration.
//
// Implementing IGameClientModule makes this assembly's route + root component
// explicit for the runtime loader. It's optional — the loader also resolves
// {RootNamespace}.GameRoot by convention — but declaring it removes the magic.
// -----------------------------------------------------------------------------

using KnockBox.Core.Client.Plugins;

namespace MyGame.Client;

/// <summary>
/// Declares the route this UI serves and the root component the WASM client renders.
/// </summary>
public sealed class GameClientModule : IGameClientModule
{
    /// <summary>Must match the server plugin's RouteIdentifier (and the @page route).</summary>
    public string RouteIdentifier => "my-game";

    /// <summary>The component the client renders for this game.</summary>
    public Type GameRootComponentType => typeof(GameRoot);
}
