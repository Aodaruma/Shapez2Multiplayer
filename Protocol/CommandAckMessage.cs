using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Protocol;

public readonly struct CommandAckMessage
{
    public CommandAckMessage(uint localCommandId, ulong globalSequence, ulong worldRevision)
    {
        LocalCommandId = localCommandId;
        GlobalSequence = globalSequence;
        WorldRevision = worldRevision;
    }

    public uint LocalCommandId { get; }

    public ulong GlobalSequence { get; }

    public ulong WorldRevision { get; }

    public byte[] Serialize()
    {
        using PacketWriter writer = new(20);
        writer.WriteUInt32(LocalCommandId);
        writer.WriteUInt64(GlobalSequence);
        writer.WriteUInt64(WorldRevision);
        return writer.ToArray();
    }

    public static CommandAckMessage Deserialize(byte[] payload)
    {
        using PacketReader reader = new(payload);
        uint localCommandId = reader.ReadUInt32();
        ulong globalSequence = reader.ReadUInt64();
        ulong worldRevision = reader.ReadUInt64();
        return new CommandAckMessage(localCommandId, globalSequence, worldRevision);
    }
}
