using System;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record RedirectHotPotatoCommand(Guid PlayerId, Guid HotPotatoCardId, Guid NewTargetPlayerId) : OperatorCommand(PlayerId);
