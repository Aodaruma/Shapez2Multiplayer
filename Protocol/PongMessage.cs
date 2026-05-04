using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Protocol;

public readonly struct PongMessage
{
    public PongMessage(uint pingId)
    {
        PingId = pingId;
    }

    public uint PingId { get; }

    public byte[] Serialize()
    {
        using PacketWriter writer = new(4);
        writer.WriteUInt32(PingId);
        return writer.ToArray();
    }

    public static PongMessage Deserialize(byte[] payload)
    {
        using PacketReader reader = new(payload);
        uint pingId = reader.ReadUInt32();
        return new PongMessage(pingId);
    }
}
