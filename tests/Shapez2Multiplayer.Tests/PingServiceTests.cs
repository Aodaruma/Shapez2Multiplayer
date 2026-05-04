using Shapez2Multiplayer.Net;
using Shapez2Multiplayer.Protocol;

namespace Shapez2Multiplayer.Tests;

public class PingServiceTests
{
    [Fact]
    public void PingService_SendsPingEverySecond()
    {
        FakeClock clock = new();
        SpyTransport transport = new(localPlayerId: 1);
        PingService service = new(transport, remotePlayerId: 2, nowMilliseconds: () => clock.Now);

        clock.Now = 0;
        service.Update();
        Assert.Equal(1, transport.SendCount);

        clock.Now = 999;
        service.Update();
        Assert.Equal(1, transport.SendCount);

        clock.Now = 1000;
        service.Update();
        Assert.Equal(2, transport.SendCount);
    }

    [Fact]
    public void PingService_UpdatesRttFromPong()
    {
        FakeClock clock = new();

        using LoopbackTransport hostTransport = new(localPlayerId: 101);
        using LoopbackTransport clientTransport = new(localPlayerId: 102);

        PingService hostPing = new(hostTransport, remotePlayerId: 102, nowMilliseconds: () => clock.Now);
        PingService clientPing = new(clientTransport, remotePlayerId: 101, nowMilliseconds: () => clock.Now);

        hostTransport.PacketReceived += hostPing.HandlePacket;
        clientTransport.PacketReceived += clientPing.HandlePacket;

        hostTransport.Start();
        clientTransport.Start();

        clock.Now = 0;
        hostPing.Update();

        clientTransport.Pump();

        clock.Now = 120;
        hostTransport.Pump();

        Assert.Equal(120, hostPing.LastRttMilliseconds);
    }

    private sealed class FakeClock
    {
        public long Now { get; set; }
    }

    private sealed class SpyTransport : INetworkTransport
    {
        public SpyTransport(uint localPlayerId)
        {
            LocalPlayerId = localPlayerId;
        }

        public uint LocalPlayerId { get; }

        public bool IsRunning => true;

        public int SendCount { get; private set; }

        public event Action<TransportPacket>? PacketReceived;

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public bool Send(uint recipientPlayerId, NetChannel channel, ReadOnlySpan<byte> payload, NetSendFlags sendFlags)
        {
            SendCount++;
            return true;
        }

        public void Pump()
        {
        }

        public void Dispose()
        {
            _ = PacketReceived;
        }
    }
}
