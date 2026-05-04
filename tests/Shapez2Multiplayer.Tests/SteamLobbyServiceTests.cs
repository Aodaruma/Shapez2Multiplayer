using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Tests;

public class SteamLobbyServiceTests
{
    [Fact]
    public void HostLobby_WhenSteamNotInitialized_ReturnsClearError()
    {
        SteamLobbyService service = new(new FakeSteamPlatformApi(isInitialized: false));

        LobbyOperationResult result = service.HostLobby();

        Assert.False(result.Success);
        Assert.Equal(NetworkError.SteamNotInitialized, result.Error);
    }

    [Fact]
    public void JoinLobby_WhenSteamNotInitialized_ReturnsClearError()
    {
        SteamLobbyService service = new(new FakeSteamPlatformApi(isInitialized: false));

        LobbyOperationResult result = service.JoinLobby(1234);

        Assert.False(result.Success);
        Assert.Equal(NetworkError.SteamNotInitialized, result.Error);
    }

    private sealed class FakeSteamPlatformApi : ISteamPlatformApi
    {
        public FakeSteamPlatformApi(bool isInitialized)
        {
            IsInitialized = isInitialized;
        }

        public bool IsInitialized { get; }

        public bool TryCreateLobby(out ulong lobbyId)
        {
            lobbyId = 999;
            return true;
        }

        public bool TryJoinLobby(ulong lobbyId)
        {
            return true;
        }

        public bool TryLeaveLobby(ulong lobbyId)
        {
            return true;
        }

        public ulong GetLobbyOwnerSteamId(ulong lobbyId)
        {
            return 1;
        }

        public ulong[] GetLobbyMemberSteamIds(ulong lobbyId)
        {
            return [1];
        }
    }
}
