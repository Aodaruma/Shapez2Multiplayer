using System;
using Core.Logging;
using Steamworks;

namespace Shapez2Multiplayer.Net;

public sealed class MultiplayerSessionController : IDisposable
{
    private readonly ILogger logger;
    private readonly ISteamPlatformApi steamApi;
    private readonly SteamLobbyService lobbyService;
    private bool disposed;

    public MultiplayerSessionController(ILogger logger, ISteamPlatformApi steamApi)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.steamApi = steamApi ?? throw new ArgumentNullException(nameof(steamApi));
        lobbyService = new SteamLobbyService(steamApi);
        StatusText = "Idle";
    }

    public bool IsInLobby { get; private set; }

    public bool IsHost { get; private set; }

    public ulong CurrentLobbyId { get; private set; }

    public string StatusText { get; private set; }

    public ulong[] CurrentMembers => IsInLobby ? steamApi.GetLobbyMemberSteamIds(CurrentLobbyId) : Array.Empty<ulong>();

    public ulong CurrentOwnerSteamId => IsInLobby ? steamApi.GetLobbyOwnerSteamId(CurrentLobbyId) : 0;

    public bool TryHostLobby(out string message)
    {
        LobbyOperationResult result = lobbyService.HostLobby();
        if (!result.Success)
        {
            message = $"Host failed: {result.Error}";
            StatusText = message;
            logger.Warning?.Log($"[MP_LOBBY] {message}");
            return false;
        }

        IsInLobby = true;
        IsHost = true;
        CurrentLobbyId = result.LobbyId;
        message = $"Hosting lobby: {CurrentLobbyId}";
        StatusText = message;
        logger.Info?.Log($"[MP_LOBBY] {message}");
        return true;
    }

    public bool TryJoinLobby(string lobbyIdText, out string message)
    {
        if (!ulong.TryParse(lobbyIdText, out ulong lobbyId))
        {
            message = "Join failed: invalid lobby id";
            StatusText = message;
            logger.Warning?.Log($"[MP_LOBBY] {message} input={lobbyIdText}");
            return false;
        }

        LobbyOperationResult result = lobbyService.JoinLobby(lobbyId);
        if (!result.Success)
        {
            message = $"Join failed: {result.Error}";
            StatusText = message;
            logger.Warning?.Log($"[MP_LOBBY] {message} lobby={lobbyId}");
            return false;
        }

        IsInLobby = true;
        IsHost = false;
        CurrentLobbyId = result.LobbyId;
        message = $"Joined lobby: {CurrentLobbyId}";
        StatusText = message;
        logger.Info?.Log($"[MP_LOBBY] {message}");
        return true;
    }

    public void LeaveLobby()
    {
        if (!IsInLobby)
        {
            return;
        }

        NetworkError error = lobbyService.LeaveLobby(CurrentLobbyId);
        logger.Info?.Log($"[MP_LOBBY] Leave lobby={CurrentLobbyId} result={error}");
        IsInLobby = false;
        IsHost = false;
        CurrentLobbyId = 0;
        StatusText = "Idle";
    }

    public void Tick()
    {
        if (steamApi.IsInitialized)
        {
            SteamClient.RunCallbacks();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        LeaveLobby();
    }
}
