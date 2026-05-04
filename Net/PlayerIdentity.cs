namespace Shapez2Multiplayer.Net;

public readonly struct PlayerIdentity
{
    public PlayerIdentity(uint playerId, ulong steamId)
    {
        PlayerId = playerId;
        SteamId = steamId;
    }

    public uint PlayerId { get; }

    public ulong SteamId { get; }
}
