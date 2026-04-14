namespace KnockBox.Core.Services.State.Games.Shared.Interfaces;

public interface IConfigurableGameState<TConfig> where TConfig : class, new()
{
    TConfig Config { get; set; }
}
