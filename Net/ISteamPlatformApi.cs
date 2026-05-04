namespace Shapez2Multiplayer.Net;

public interface ISteamPlatformApi
{
    bool IsInitialized { get; }

    bool TryCreateLobby(out ulong lobbyId);

    bool TryJoinLobby(ulong lobbyId);
}
