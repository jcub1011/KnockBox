using KnockBox.Core.Plugins;
using KnockBox.LinkedList.Components;
using KnockBox.LinkedList.Services.Logic;
using KnockBox.LinkedList.Services.Logic.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.LinkedList
{
    public class LinkedListModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(LinkedListModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
        {
            registration.AddSingleton<WordPairSource>(_ => new WordPairSource());
            registration.AddGameEngine<LinkedListGameEngine>();
        }

        public RenderFragment? GetCustomHeader() => builder =>
        {
            builder.OpenComponent<LinkedListHeader>(0);
            builder.CloseComponent();
        };
    }
}
