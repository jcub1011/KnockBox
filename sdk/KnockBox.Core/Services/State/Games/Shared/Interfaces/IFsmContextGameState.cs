namespace KnockBox.Core.Services.State.Games.Shared.Interfaces;

/// <summary>
/// Marker interface for game states driven by a
/// <see cref="FiniteStateMachine{TContext,TCommand}"/>. <see cref="Context"/>
/// holds the data the FSM's nodes read and mutate — it travels with the state
/// and is what <c>IGameState&lt;TContext,TCommand&gt;</c> nodes operate on.
/// </summary>
/// <typeparam name="TContext">
/// The context record consumed by this game's FSM nodes.
/// </typeparam>
public interface IFsmContextGameState<TContext>
{
    /// <summary>The FSM context payload, or null if the FSM hasn't started.</summary>
    TContext? Context { get; set; }
}
