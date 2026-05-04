using Shapez2Multiplayer.Protocol;

namespace Shapez2Multiplayer.Tests;

public class HandshakeMessageTests
{
    [Fact]
    public void HelloMessage_CanRoundtrip()
    {
        HelloMessage message = new(steamId: 76561198000000001);

        byte[] bytes = message.Serialize();
        HelloMessage actual = HelloMessage.Deserialize(bytes);

        Assert.Equal(message.SteamId, actual.SteamId);
    }

    [Fact]
    public void WelcomeMessage_CanRoundtrip()
    {
        WelcomeMessage message = new(steamId: 76561198000000002);

        byte[] bytes = message.Serialize();
        WelcomeMessage actual = WelcomeMessage.Deserialize(bytes);

        Assert.Equal(message.SteamId, actual.SteamId);
    }
}
