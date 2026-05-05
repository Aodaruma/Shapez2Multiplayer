using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Protocol;

public readonly struct WorldHashMessage
{
    public WorldHashMessage(ulong worldRevision, ulong layoutHash)
    {
        WorldRevision = worldRevision;
        LayoutHash = layoutHash;
    }

    public ulong WorldRevision { get; }

    public ulong LayoutHash { get; }

    public byte[] Serialize()
    {
        using PacketWriter writer = new(capacity: 16);
        writer.WriteUInt64(WorldRevision);
        writer.WriteUInt64(LayoutHash);
        return writer.ToArray();
    }

    public static WorldHashMessage Deserialize(byte[] payload)
    {
        using PacketReader reader = new(payload);
        ulong worldRevision = reader.ReadUInt64();
        ulong layoutHash = reader.ReadUInt64();
        return new WorldHashMessage(worldRevision, layoutHash);
    }
}
