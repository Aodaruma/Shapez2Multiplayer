namespace Shapez2Multiplayer.Net;

public readonly struct LobbyOperationResult
{
    private LobbyOperationResult(bool success, NetworkError error, ulong lobbyId, uint localPlayerId)
    {
        Success = success;
        Error = error;
        LobbyId = lobbyId;
        LocalPlayerId = localPlayerId;
    }

    public bool Success { get; }

    public NetworkError Error { get; }

    public ulong LobbyId { get; }

    public uint LocalPlayerId { get; }

    public static LobbyOperationResult Succeeded(ulong lobbyId, uint localPlayerId) =>
        new(success: true, error: NetworkError.None, lobbyId: lobbyId, localPlayerId: localPlayerId);

    public static LobbyOperationResult Failed(NetworkError error) =>
        new(success: false, error: error, lobbyId: 0, localPlayerId: 0);
}
