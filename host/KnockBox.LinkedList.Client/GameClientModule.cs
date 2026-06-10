using KnockBox.Core.Client.Plugins;
using KnockBox.LinkedList.Client.Components;

namespace KnockBox.LinkedList.Client
{
    /// <summary>
    /// The client-side plugin module the WASM runtime loader discovers in the streamed assembly. It
    /// names the route (matching the server <c>IGameModule.RouteIdentifier</c> and the
    /// <c>@page</c>/manifest) and the root component to render in <c>RuntimeGameLobby</c>.
    /// </summary>
    public sealed class GameClientModule : IGameClientModule
    {
        public string RouteIdentifier => "linked-list";
        public Type GameRootComponentType => typeof(GameRoot);
    }
}
