using System;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record PlayReactionCommand(Guid PlayerId, Guid ShieldCardId) : OperatorCommand(PlayerId);
