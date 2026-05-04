using System;
using Shapez2Multiplayer.Commands;
using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Protocol;

public readonly struct AuthoritativeCommandMessage
{
    public AuthoritativeCommandMessage(ulong globalSequence, ulong worldRevision, ICommand command)
    {
        GlobalSequence = globalSequence;
        WorldRevision = worldRevision;
        Command = command ?? throw new ArgumentNullException(nameof(command));
    }

    public ulong GlobalSequence { get; }

    public ulong WorldRevision { get; }

    public ICommand Command { get; }

    public byte[] Serialize()
    {
        byte[] commandBytes = CommandSerializer.Serialize(Command);

        using PacketWriter writer = new(commandBytes.Length + 20);
        writer.WriteUInt64(GlobalSequence);
        writer.WriteUInt64(WorldRevision);
        writer.WriteBytesWithLength(commandBytes, ProtocolLimits.MaxPacketBytes);
        return writer.ToArray();
    }

    public static AuthoritativeCommandMessage Deserialize(byte[] payload)
    {
        using PacketReader reader = new(payload);
        ulong globalSequence = reader.ReadUInt64();
        ulong worldRevision = reader.ReadUInt64();
        byte[] commandBytes = reader.ReadBytesWithLength(ProtocolLimits.MaxPacketBytes);
        ICommand command = CommandSerializer.Deserialize(commandBytes);
        return new AuthoritativeCommandMessage(globalSequence, worldRevision, command);
    }
}
