using System;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record DrawCardsCommand(string PlayerId) : OperatorCommand(PlayerId);
