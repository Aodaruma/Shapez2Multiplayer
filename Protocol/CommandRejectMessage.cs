using System;
using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Protocol;

public readonly struct CommandRejectMessage
{
    private const int MaxReasonTextBytes = 512;

    public CommandRejectMessage(uint localCommandId, CommandRejectReason reason, string reasonText = "")
    {
        LocalCommandId = localCommandId;
        Reason = reason;
        ReasonText = reasonText ?? string.Empty;
    }

    public uint LocalCommandId { get; }

    public CommandRejectReason Reason { get; }

    public string ReasonText { get; }

    public byte[] Serialize()
    {
        using PacketWriter writer = new();
        writer.WriteUInt32(LocalCommandId);
        writer.WriteUInt16((ushort)Reason);
        writer.WriteString(ReasonText, MaxReasonTextBytes);
        return writer.ToArray();
    }

    public static CommandRejectMessage Deserialize(byte[] payload)
    {
        using PacketReader reader = new(payload);
        uint localCommandId = reader.ReadUInt32();
        CommandRejectReason reason = (CommandRejectReason)reader.ReadUInt16();
        string reasonText = reader.ReadString(MaxReasonTextBytes);
        return new CommandRejectMessage(localCommandId, reason, reasonText);
    }
}
