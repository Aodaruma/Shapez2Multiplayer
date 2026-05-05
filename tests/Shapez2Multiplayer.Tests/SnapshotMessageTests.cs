using Shapez2Multiplayer.Protocol;

namespace Shapez2Multiplayer.Tests;

public class SnapshotMessageTests
{
    [Fact]
    public void SnapshotBeginMessage_Roundtrip()
    {
        SnapshotBeginMessage expected = new(snapshotId: 12, worldRevision: 34, totalChunks: 5, totalBytes: 12345, layoutHash: 99);

        byte[] bytes = expected.Serialize();
        SnapshotBeginMessage actual = SnapshotBeginMessage.Deserialize(bytes);

        Assert.Equal(expected.SnapshotId, actual.SnapshotId);
        Assert.Equal(expected.WorldRevision, actual.WorldRevision);
        Assert.Equal(expected.TotalChunks, actual.TotalChunks);
        Assert.Equal(expected.TotalBytes, actual.TotalBytes);
        Assert.Equal(expected.LayoutHash, actual.LayoutHash);
    }

    [Fact]
    public void SnapshotChunkMessage_Roundtrip()
    {
        SnapshotChunkMessage expected = new(snapshotId: 77, chunkIndex: 2, chunkData: [1, 2, 3, 4, 5]);

        byte[] bytes = expected.Serialize();
        SnapshotChunkMessage actual = SnapshotChunkMessage.Deserialize(bytes);

        Assert.Equal(expected.SnapshotId, actual.SnapshotId);
        Assert.Equal(expected.ChunkIndex, actual.ChunkIndex);
        Assert.Equal(expected.ChunkData, actual.ChunkData);
    }

    [Fact]
    public void SnapshotEndMessage_Roundtrip()
    {
        SnapshotEndMessage expected = new(snapshotId: 999);

        byte[] bytes = expected.Serialize();
        SnapshotEndMessage actual = SnapshotEndMessage.Deserialize(bytes);

        Assert.Equal(expected.SnapshotId, actual.SnapshotId);
    }

    [Fact]
    public void WorldHashMessage_Roundtrip()
    {
        WorldHashMessage expected = new(worldRevision: 111, layoutHash: 222);

        byte[] bytes = expected.Serialize();
        WorldHashMessage actual = WorldHashMessage.Deserialize(bytes);

        Assert.Equal(expected.WorldRevision, actual.WorldRevision);
        Assert.Equal(expected.LayoutHash, actual.LayoutHash);
    }
}
