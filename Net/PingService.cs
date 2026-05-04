using System;
using System.Collections.Generic;
using Shapez2Multiplayer.Protocol;

namespace Shapez2Multiplayer.Net;

public sealed class PingService
{
    private readonly INetworkTransport transport;
    private readonly uint remotePlayerId;
    private readonly Func<long> nowMilliseconds;
    private readonly Dictionary<uint, long> pendingPings = new();

    private long nextPingAtMilliseconds;
    private uint nextPingId = 1;

    public PingService(INetworkTransport transport, uint remotePlayerId, Func<long> nowMilliseconds)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.remotePlayerId = remotePlayerId;
        this.nowMilliseconds = nowMilliseconds ?? throw new ArgumentNullException(nameof(nowMilliseconds));
    }

    public int? LastRttMilliseconds { get; private set; }

    public event Action<int>? RttUpdated;

    public void Update()
    {
        long now = nowMilliseconds();
        if (now < nextPingAtMilliseconds)
        {
            return;
        }

        uint pingId = nextPingId++;
        pendingPings[pingId] = now;
        nextPingAtMilliseconds = now + 1000;

        PingMessage ping = new(pingId);
        PacketHeader header = new(
            protocolVersion: ProtocolLimits.ProtocolVersion,
            messageType: MessageType.Ping,
            sessionId: 1,
            senderPlayerId: transport.LocalPlayerId,
            sequence: pingId,
            ackSequence: 0,
            worldRevision: 0);
        byte[] packetBytes = PacketSerializer.Serialize(header, ping.Serialize());

        _ = transport.Send(remotePlayerId, NetChannel.Control, packetBytes, NetSendFlags.Reliable);
    }

    public void HandlePacket(TransportPacket packet)
    {
        if (!PacketSerializer.TryDeserialize(packet.Payload, out PacketHeader header, out byte[] payload, out _))
        {
            return;
        }

        switch (header.MessageType)
        {
            case MessageType.Ping:
            {
                PingMessage ping = PingMessage.Deserialize(payload);
                SendPong(ping.PingId, packet.SenderPlayerId);
                break;
            }
            case MessageType.Pong:
            {
                PongMessage pong = PongMessage.Deserialize(payload);
                OnPong(pong.PingId);
                break;
            }
        }
    }

    private void SendPong(uint pingId, uint recipientPlayerId)
    {
        PongMessage pong = new(pingId);
        PacketHeader header = new(
            protocolVersion: ProtocolLimits.ProtocolVersion,
            messageType: MessageType.Pong,
            sessionId: 1,
            senderPlayerId: transport.LocalPlayerId,
            sequence: pingId,
            ackSequence: pingId,
            worldRevision: 0);
        byte[] packetBytes = PacketSerializer.Serialize(header, pong.Serialize());

        _ = transport.Send(recipientPlayerId, NetChannel.Control, packetBytes, NetSendFlags.Reliable);
    }

    private void OnPong(uint pingId)
    {
        if (!pendingPings.TryGetValue(pingId, out long sentAt))
        {
            return;
        }

        pendingPings.Remove(pingId);
        int rtt = checked((int)(nowMilliseconds() - sentAt));
        LastRttMilliseconds = rtt;
        RttUpdated?.Invoke(rtt);
    }
}
