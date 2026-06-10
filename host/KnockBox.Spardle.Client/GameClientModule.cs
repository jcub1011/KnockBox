using KnockBox.Core.Client.Plugins;
using KnockBox.Spardle.Client.Components;

namespace KnockBox.Spardle.Client
{
    /// <summary>
    /// The client-side plugin module the WASM runtime loader discovers in the streamed assembly. It
    /// names the route (matching the server <c>IGameModule.RouteIdentifier</c> and the manifest) and
    /// the root component to render in <c>RuntimeGameLobby</c>.
    /// </summary>
    public sealed class GameClientModule : IGameClientModule
    {
        public string RouteIdentifier => "spardle";
        public Type GameRootComponentType => typeof(GameRoot);
    }
}
