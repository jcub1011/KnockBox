using System;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record RedirectHotPotatoCommand(string PlayerId, Guid HotPotatoCardId, string NewTargetPlayerId) : OperatorCommand(PlayerId);
