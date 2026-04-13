using KnockBox.Core.Plugins;
using KnockBox.Operator.Components;
using KnockBox.Operator.Services.Logic.Games;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Operator
{
    public class OperatorModule : IGameModule
    {
        public string Name => "Operator";
        public string Description => "Decode and transmit the right signal.";
        public string RouteIdentifier => "operator";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddGameEngine<OperatorGameEngine>(RouteIdentifier);
        }

        public RenderFragment GetButtonContent() => builder =>
        {
            builder.OpenComponent<OperatorTile>(0);
            builder.CloseComponent();
        };
    }
}
