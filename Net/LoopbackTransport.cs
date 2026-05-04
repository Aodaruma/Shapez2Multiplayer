using System;
using System.Collections.Concurrent;

namespace Shapez2Multiplayer.Net;

public sealed class LoopbackTransport : INetworkTransport
{
    private static readonly ConcurrentDictionary<uint, LoopbackTransport> Registry = new();

    private readonly ConcurrentQueue<TransportPacket> inboundQueue = new();
    private bool disposed;

    public LoopbackTransport(uint localPlayerId)
    {
        LocalPlayerId = localPlayerId;
    }

    public uint LocalPlayerId { get; }

    public bool IsRunning { get; private set; }

    public event Action<TransportPacket>? PacketReceived;

    public void Start()
    {
        ThrowIfDisposed();
        if (IsRunning)
        {
            return;
        }

        if (!Registry.TryAdd(LocalPlayerId, this))
        {
            throw new InvalidOperationException($"Loopback transport already registered for player {LocalPlayerId}");
        }

        IsRunning = true;
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        Registry.TryRemove(LocalPlayerId, out _);
        while (inboundQueue.TryDequeue(out _))
        {
        }
    }

    public bool Send(uint recipientPlayerId, NetChannel channel, ReadOnlySpan<byte> payload, NetSendFlags sendFlags)
    {
        ThrowIfDisposed();
        if (!IsRunning)
        {
            return false;
        }

        if (!Registry.TryGetValue(recipientPlayerId, out LoopbackTransport? recipient) || !recipient.IsRunning)
        {
            return false;
        }

        recipient.inboundQueue.Enqueue(
            new TransportPacket(
                senderPlayerId: LocalPlayerId,
                recipientPlayerId: recipientPlayerId,
                channel: channel,
                sendFlags: sendFlags,
                payload: payload.ToArray()));
        return true;
    }

    public void Pump()
    {
        ThrowIfDisposed();
        while (inboundQueue.TryDequeue(out TransportPacket packet))
        {
            PacketReceived?.Invoke(packet);
        }
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
            throw new ObjectDisposedException(nameof(LoopbackTransport));
        }
    }
}
