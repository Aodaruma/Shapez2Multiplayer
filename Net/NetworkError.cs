namespace Shapez2Multiplayer.Net;

public enum NetworkError
{
    None,
    SteamNotInitialized,
    LobbyCreateFailed,
    LobbyJoinFailed,
    PeerTimeout,
    PacketTooLarge,
    InvalidMagic,
    InvalidProtocol,
    DeserializationFailed,
    TransportSendFailed
}
