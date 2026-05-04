using Shapez2Multiplayer.Commands;

namespace Shapez2Multiplayer.Authority;

public class CommandValidator
{
    public virtual CommandValidationResult Validate(uint playerId, ICommand command)
    {
        _ = playerId;
        _ = command;
        return CommandValidationResult.Valid();
    }
}
