using System;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record PassReactionCommand(string PlayerId) : OperatorCommand(PlayerId);
