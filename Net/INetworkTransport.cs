using System;

namespace Shapez2Multiplayer.Net;

public interface INetworkTransport : IDisposable
{
    uint LocalPlayerId { get; }

    bool IsRunning { get; }

    event Action<TransportPacket>? PacketReceived;

    void Start();

    void Stop();

    bool Send(uint recipientPlayerId, NetChannel channel, ReadOnlySpan<byte> payload, NetSendFlags sendFlags);

    void Pump();
}
