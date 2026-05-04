using System;
using Shapez2Multiplayer.Commands;
using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Protocol;

public readonly struct ClientCommandMessage
{
    public ClientCommandMessage(ICommand command)
    {
        Command = command ?? throw new ArgumentNullException(nameof(command));
    }

    public ICommand Command { get; }

    public byte[] Serialize()
    {
        byte[] commandBytes = CommandSerializer.Serialize(Command);
        using PacketWriter writer = new(commandBytes.Length + 4);
        writer.WriteBytesWithLength(commandBytes, ProtocolLimits.MaxPacketBytes);
        return writer.ToArray();
    }

    public static ClientCommandMessage Deserialize(byte[] payload)
    {
        using PacketReader reader = new(payload);
        byte[] commandBytes = reader.ReadBytesWithLength(ProtocolLimits.MaxPacketBytes);
        ICommand command = CommandSerializer.Deserialize(commandBytes);
        return new ClientCommandMessage(command);
    }
}
