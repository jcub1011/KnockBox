using System;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record PlayReactionCommand(string PlayerId, Guid ShieldCardId) : OperatorCommand(PlayerId);
