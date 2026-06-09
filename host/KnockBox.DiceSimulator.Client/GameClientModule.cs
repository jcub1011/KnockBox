using KnockBox.Core.Client.Plugins;

namespace KnockBox.DiceSimulator.Client;

/// <summary>
/// Declares the route this UI serves and the root component the WASM client renders.
/// Optional — the loader also resolves <c>{RootNamespace}.GameRoot</c> by convention —
/// but declaring it removes the magic.
/// </summary>
public sealed class GameClientModule : IGameClientModule
{
    public string RouteIdentifier => "dice-simulator";

    public Type GameRootComponentType => typeof(GameRoot);
}
