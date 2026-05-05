using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Protocol;

public readonly struct SnapshotEndMessage
{
    public SnapshotEndMessage(uint snapshotId)
    {
        SnapshotId = snapshotId;
    }

    public uint SnapshotId { get; }

    public byte[] Serialize()
    {
        using PacketWriter writer = new(capacity: 4);
        writer.WriteUInt32(SnapshotId);
        return writer.ToArray();
    }

    public static SnapshotEndMessage Deserialize(byte[] payload)
    {
        using PacketReader reader = new(payload);
        return new SnapshotEndMessage(reader.ReadUInt32());
    }
}
