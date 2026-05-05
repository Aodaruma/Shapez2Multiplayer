using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Protocol;

public readonly struct SnapshotBeginMessage
{
    public SnapshotBeginMessage(uint snapshotId, ulong worldRevision, int totalChunks, int totalBytes, ulong layoutHash)
    {
        SnapshotId = snapshotId;
        WorldRevision = worldRevision;
        TotalChunks = totalChunks;
        TotalBytes = totalBytes;
        LayoutHash = layoutHash;
    }

    public uint SnapshotId { get; }

    public ulong WorldRevision { get; }

    public int TotalChunks { get; }

    public int TotalBytes { get; }

    public ulong LayoutHash { get; }

    public byte[] Serialize()
    {
        using PacketWriter writer = new(capacity: 28);
        writer.WriteUInt32(SnapshotId);
        writer.WriteUInt64(WorldRevision);
        writer.WriteInt32(TotalChunks);
        writer.WriteInt32(TotalBytes);
        writer.WriteUInt64(LayoutHash);
        return writer.ToArray();
    }

    public static SnapshotBeginMessage Deserialize(byte[] payload)
    {
        using PacketReader reader = new(payload);
        uint snapshotId = reader.ReadUInt32();
        ulong worldRevision = reader.ReadUInt64();
        int totalChunks = reader.ReadInt32();
        int totalBytes = reader.ReadInt32();
        ulong layoutHash = reader.ReadUInt64();
        return new SnapshotBeginMessage(snapshotId, worldRevision, totalChunks, totalBytes, layoutHash);
    }
}
