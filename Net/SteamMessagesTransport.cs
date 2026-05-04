using System;

namespace Shapez2Multiplayer.Net;

public sealed class SteamMessagesTransport : INetworkTransport
{
    private bool disposed;

    public SteamMessagesTransport(uint localPlayerId)
    {
        LocalPlayerId = localPlayerId;
    }

    public uint LocalPlayerId { get; }

    public bool IsRunning { get; private set; }

    public event Action<TransportPacket>? PacketReceived;

    public void Start()
    {
        ThrowIfDisposed();
        IsRunning = true;
        // TODO: SteamMessages API の初期化と callback registration.
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        // TODO: SteamMessages API の cleanup.
    }

    public bool Send(uint recipientPlayerId, NetChannel channel, ReadOnlySpan<byte> payload, NetSendFlags sendFlags)
    {
        ThrowIfDisposed();
        if (!IsRunning)
        {
            return false;
        }

        // TODO: SteamMessages 送信実装.
        return false;
    }

    public void Pump()
    {
        ThrowIfDisposed();
        // TODO: Steam callback polling + packet dispatch.
        _ = PacketReceived;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Stop();
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(SteamMessagesTransport));
        }
    }
}
