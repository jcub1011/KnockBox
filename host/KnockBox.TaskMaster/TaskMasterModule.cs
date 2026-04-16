using KnockBox.Core.Plugins;
using KnockBox.TaskMaster.Components;
using KnockBox.TaskMaster.Services.Logic.Games;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.TaskMaster
{
    public class TaskMasterModule : IGameModule
    {
        public string Name => "Task Master";
        public string Description => "Complete tasks before time runs out.";
        public string RouteIdentifier => "task-master";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddGameEngine<TaskMasterGameEngine>(RouteIdentifier);
        }

        public RenderFragment GetButtonContent() => builder =>
        {
            builder.OpenComponent<TaskMasterTile>(0);
            builder.CloseComponent();
        };
    }
}
