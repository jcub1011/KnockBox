using System;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record PassReactionCommand(Guid PlayerId) : OperatorCommand(PlayerId);
