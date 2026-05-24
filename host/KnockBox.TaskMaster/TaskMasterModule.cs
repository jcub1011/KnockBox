using KnockBox.Core.Plugins;
using KnockBox.TaskMaster.Components;
using KnockBox.TaskMaster.Services.Logic.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.TaskMaster
{
    public class TaskMasterModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(TaskMasterModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
            => registration.AddGameEngine<TaskMasterGameEngine>();

        public RenderFragment? GetCustomHeader() => builder =>
        {
            builder.OpenComponent<TaskMasterHeader>(0);
            builder.CloseComponent();
        };
    }
}
