using System;

namespace Shapez2Multiplayer.Net;

public sealed class SteamLobbyService
{
    private readonly ISteamPlatformApi steamApi;
    private uint nextPlayerId = 1;

    public SteamLobbyService(ISteamPlatformApi steamApi)
    {
        this.steamApi = steamApi ?? throw new ArgumentNullException(nameof(steamApi));
    }

    public LobbyOperationResult HostLobby()
    {
        if (!steamApi.IsInitialized)
        {
            return LobbyOperationResult.Failed(NetworkError.SteamNotInitialized);
        }

        if (!steamApi.TryCreateLobby(out ulong lobbyId))
        {
            return LobbyOperationResult.Failed(NetworkError.LobbyCreateFailed);
        }

        uint localPlayerId = AllocatePlayerId();
        return LobbyOperationResult.Succeeded(lobbyId, localPlayerId);
    }

    public LobbyOperationResult JoinLobby(ulong lobbyId)
    {
        if (!steamApi.IsInitialized)
        {
            return LobbyOperationResult.Failed(NetworkError.SteamNotInitialized);
        }

        if (!steamApi.TryJoinLobby(lobbyId))
        {
            return LobbyOperationResult.Failed(NetworkError.LobbyJoinFailed);
        }

        uint localPlayerId = AllocatePlayerId();
        return LobbyOperationResult.Succeeded(lobbyId, localPlayerId);
    }

    public NetworkError LeaveLobby(ulong lobbyId)
    {
        if (!steamApi.IsInitialized)
        {
            return NetworkError.SteamNotInitialized;
        }

        return steamApi.TryLeaveLobby(lobbyId) ? NetworkError.None : NetworkError.LobbyJoinFailed;
    }

    private uint AllocatePlayerId() => nextPlayerId++;
}
