namespace KnockBox.Core.Services.State.Games.Shared.Interfaces;

public interface IFsmContextGameState<TContext>
{
    TContext? Context { get; set; }
}
