using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Protocol;

public readonly struct HelloMessage
{
    public HelloMessage(ulong steamId)
    {
        SteamId = steamId;
    }

    public ulong SteamId { get; }

    public byte[] Serialize()
    {
        using PacketWriter writer = new(8);
        writer.WriteUInt64(SteamId);
        return writer.ToArray();
    }

    public static HelloMessage Deserialize(byte[] payload)
    {
        using PacketReader reader = new(payload);
        return new HelloMessage(reader.ReadUInt64());
    }
}
