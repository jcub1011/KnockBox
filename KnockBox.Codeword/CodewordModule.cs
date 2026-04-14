using KnockBox.Codeword.Components;
using KnockBox.Codeword.Services.Logic.Games;
using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Codeword
{
    public class CodewordModule : IGameModule
    {
        public string Name => "Codeword";
        public string Description => "A social card-based fortune teller.";
        public string RouteIdentifier => "codeword";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddGameEngine<CodewordGameEngine>(RouteIdentifier);
        }

        public RenderFragment GetButtonContent() => builder =>
        {
            builder.OpenComponent<CodewordTile>(0);
            builder.CloseComponent();
        };
    }
}
