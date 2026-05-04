using System;
using System.IO;
using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Commands;

public static class CommandSerializer
{
    public static byte[] Serialize(ICommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        using PacketWriter writer = new();
        command.Serialize(writer);
        return writer.ToArray();
    }

    public static ICommand Deserialize(byte[] payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        using PacketReader reader = new(payload);
        CommandType commandType = (CommandType)reader.ReadUInt16();
        ICommand command = commandType switch
        {
            CommandType.Build => BuildCommand.DeserializeAfterType(reader),
            CommandType.Delete => DeleteCommand.DeserializeAfterType(reader),
            _ => throw new InvalidDataException($"Unsupported command type: {commandType}")
        };

        if (reader.RemainingBytes != 0)
        {
            throw new InvalidDataException($"Trailing bytes detected after command deserialize: {reader.RemainingBytes}");
        }

        return command;
    }
}
