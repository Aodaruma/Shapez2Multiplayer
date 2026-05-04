namespace Shapez2Multiplayer.Net;

public readonly struct TransportPacket
{
    public TransportPacket(
        uint senderPlayerId,
        uint recipientPlayerId,
        NetChannel channel,
        NetSendFlags sendFlags,
        byte[] payload)
    {
        SenderPlayerId = senderPlayerId;
        RecipientPlayerId = recipientPlayerId;
        Channel = channel;
        SendFlags = sendFlags;
        Payload = payload;
    }

    public uint SenderPlayerId { get; }

    public uint RecipientPlayerId { get; }

    public NetChannel Channel { get; }

    public NetSendFlags SendFlags { get; }

    public byte[] Payload { get; }
}
