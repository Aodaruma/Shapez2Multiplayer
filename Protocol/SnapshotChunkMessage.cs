using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Protocol;

public readonly struct SnapshotChunkMessage
{
    public SnapshotChunkMessage(uint snapshotId, int chunkIndex, byte[] chunkData)
    {
        SnapshotId = snapshotId;
        ChunkIndex = chunkIndex;
        ChunkData = chunkData ?? throw new System.ArgumentNullException(nameof(chunkData));
    }

    public uint SnapshotId { get; }

    public int ChunkIndex { get; }

    public byte[] ChunkData { get; }

    public byte[] Serialize()
    {
        using PacketWriter writer = new(capacity: ChunkData.Length + 12);
        writer.WriteUInt32(SnapshotId);
        writer.WriteInt32(ChunkIndex);
        writer.WriteBytesWithLength(ChunkData, ProtocolLimits.MaxSnapshotChunkBytes);
        return writer.ToArray();
    }

    public static SnapshotChunkMessage Deserialize(byte[] payload)
    {
        using PacketReader reader = new(payload);
        uint snapshotId = reader.ReadUInt32();
        int chunkIndex = reader.ReadInt32();
        byte[] chunkData = reader.ReadBytesWithLength(ProtocolLimits.MaxSnapshotChunkBytes);
        return new SnapshotChunkMessage(snapshotId, chunkIndex, chunkData);
    }
}
