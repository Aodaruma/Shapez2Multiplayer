using Shapez2Multiplayer.Protocol;

namespace Shapez2Multiplayer.Authority;

public readonly struct CommandValidationResult
{
    private CommandValidationResult(bool success, CommandRejectReason reason)
    {
        Success = success;
        Reason = reason;
    }

    public bool Success { get; }

    public CommandRejectReason Reason { get; }

    public static CommandValidationResult Valid() => new(success: true, reason: CommandRejectReason.Unknown);

    public static CommandValidationResult Invalid(CommandRejectReason reason) => new(success: false, reason: reason);
}
