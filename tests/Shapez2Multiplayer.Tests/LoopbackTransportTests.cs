using System.Collections.Generic;
using Shapez2Multiplayer.Net;
using Shapez2Multiplayer.Protocol;

namespace Shapez2Multiplayer.Tests;

public class LoopbackTransportTests
{
    [Fact]
    public void LoopbackTransport_CanExchangeHelloAndWelcome()
    {
        using LoopbackTransport host = new(localPlayerId: 1);
        using LoopbackTransport client = new(localPlayerId: 2);

        List<MessageType> hostReceived = [];
        List<MessageType> clientReceived = [];

        host.PacketReceived += packet => hostReceived.Add(ReadMessageType(packet.Payload));
        client.PacketReceived += packet => clientReceived.Add(ReadMessageType(packet.Payload));

        host.Start();
        client.Start();

        bool sentHello = client.Send(
            recipientPlayerId: 1,
            channel: NetChannel.Control,
            payload: CreatePacketBytes(MessageType.Hello, senderPlayerId: 2),
            sendFlags: NetSendFlags.Reliable);
        Assert.True(sentHello);

        host.Pump();
        Assert.Single(hostReceived);
        Assert.Equal(MessageType.Hello, hostReceived[0]);

        bool sentWelcome = host.Send(
            recipientPlayerId: 2,
            channel: NetChannel.Control,
            payload: CreatePacketBytes(MessageType.Welcome, senderPlayerId: 1),
            sendFlags: NetSendFlags.Reliable);
        Assert.True(sentWelcome);

        client.Pump();
        Assert.Single(clientReceived);
        Assert.Equal(MessageType.Welcome, clientReceived[0]);
    }

    private static byte[] CreatePacketBytes(MessageType messageType, uint senderPlayerId)
    {
        PacketHeader header = new(
            protocolVersion: ProtocolLimits.ProtocolVersion,
            messageType: messageType,
            sessionId: 42,
            senderPlayerId: senderPlayerId,
            sequence: 1,
            ackSequence: 0,
            worldRevision: 0);
        return PacketSerializer.Serialize(header, payload: []);
    }

    private static MessageType ReadMessageType(byte[] payload)
    {
        bool ok = PacketSerializer.TryDeserialize(payload, out PacketHeader header, out _, out NetworkError error);
        Assert.True(ok, $"Deserialize failed: {error}");
        return header.MessageType;
    }
}
