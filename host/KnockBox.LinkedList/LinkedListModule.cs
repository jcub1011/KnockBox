using KnockBox.Core.Plugins;
using KnockBox.LinkedList.Components;
using KnockBox.LinkedList.Services.Logic;
using KnockBox.LinkedList.Services.Logic.Games;
using KnockBox.LinkedList.Services.Storage;
using Microsoft.AspNetCore.Components;

namespace KnockBox.LinkedList
{
    public class LinkedListModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(LinkedListModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
        {
            registration.AddSingleton<WordSource, WordSource>();
            registration.AddGameEngine<LinkedListGameEngine>();
            registration.AddScoped<LinkedListStorage, LinkedListStorage>();
        }

        public RenderFragment? GetCustomHeader() => builder =>
        {
            builder.OpenComponent<LinkedListHeader>(0);
            builder.CloseComponent();
        };
    }
}
