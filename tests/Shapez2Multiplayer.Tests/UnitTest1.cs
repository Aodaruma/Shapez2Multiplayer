using System.IO;
using Shapez2Multiplayer.Net;
using Shapez2Multiplayer.Protocol;

namespace Shapez2Multiplayer.Tests;

public class PacketSerializerTests
{
    [Fact]
    public void HeaderRoundtrip_Succeeds()
    {
        PacketHeader header = new(
            ProtocolLimits.ProtocolVersion,
            MessageType.Hello,
            sessionId: 10,
            senderPlayerId: 2,
            sequence: 100,
            ackSequence: 99,
            worldRevision: 1234);
        byte[] payload = [1, 2, 3, 4, 5];

        byte[] bytes = PacketSerializer.Serialize(header, payload);
        bool ok = PacketSerializer.TryDeserialize(bytes, out PacketHeader actualHeader, out byte[] actualPayload, out NetworkError error);

        Assert.True(ok);
        Assert.Equal(NetworkError.None, error);
        Assert.Equal(PacketHeader.MagicValue, actualHeader.Magic);
        Assert.Equal(header.ProtocolVersion, actualHeader.ProtocolVersion);
        Assert.Equal(header.MessageType, actualHeader.MessageType);
        Assert.Equal(header.SessionId, actualHeader.SessionId);
        Assert.Equal(header.SenderPlayerId, actualHeader.SenderPlayerId);
        Assert.Equal(header.Sequence, actualHeader.Sequence);
        Assert.Equal(header.AckSequence, actualHeader.AckSequence);
        Assert.Equal(header.WorldRevision, actualHeader.WorldRevision);
        Assert.Equal(payload, actualPayload);
    }

    [Fact]
    public void InvalidMagic_IsRejected()
    {
        PacketHeader header = new(
            ProtocolLimits.ProtocolVersion,
            MessageType.Ping,
            sessionId: 1,
            senderPlayerId: 1,
            sequence: 1,
            ackSequence: 0,
            worldRevision: 1);

        byte[] bytes = PacketSerializer.Serialize(header, [9, 9, 9]);
        bytes[0] = 0x00;

        bool ok = PacketSerializer.TryDeserialize(bytes, out _, out _, out NetworkError error);

        Assert.False(ok);
        Assert.Equal(NetworkError.InvalidMagic, error);
    }

    [Fact]
    public void MaxPacketSizeExceeded_IsRejected()
    {
        byte[] tooLargePayload = new byte[ProtocolLimits.MaxPacketBytes - PacketHeader.SizeInBytes + 1];

        PacketHeader header = new(
            ProtocolLimits.ProtocolVersion,
            MessageType.Chat,
            sessionId: 77,
            senderPlayerId: 5,
            sequence: 44,
            ackSequence: 43,
            worldRevision: 9876);

        Assert.Throws<InvalidDataException>(() => PacketSerializer.Serialize(header, tooLargePayload));

        byte[] tooLargePacket = new byte[ProtocolLimits.MaxPacketBytes + 1];
        bool ok = PacketSerializer.TryDeserialize(tooLargePacket, out _, out _, out NetworkError error);

        Assert.False(ok);
        Assert.Equal(NetworkError.PacketTooLarge, error);
    }
}
