namespace Shapez2Multiplayer.Net;

public interface ISteamPlatformApi
{
    bool IsInitialized { get; }

    bool TryCreateLobby(out ulong lobbyId);

    bool TryJoinLobby(ulong lobbyId);

    bool TryLeaveLobby(ulong lobbyId);

    ulong GetLobbyOwnerSteamId(ulong lobbyId);

    ulong[] GetLobbyMemberSteamIds(ulong lobbyId);
}
