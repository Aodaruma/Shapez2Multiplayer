namespace Shapez2Multiplayer.Protocol;

public enum MessageType : ushort
{
    Hello = 1,
    Welcome = 2,
    JoinRequest = 3,
    JoinAccept = 4,
    JoinReject = 5,
    SnapshotBegin = 10,
    SnapshotChunk = 11,
    SnapshotEnd = 12,
    ClientCommand = 20,
    AuthoritativeCommand = 21,
    CommandAck = 22,
    CommandReject = 23,
    WorldHash = 30,
    DesyncReport = 31,
    ResyncRequest = 32,
    PlayerCursor = 40,
    PlayerSelection = 41,
    Chat = 42,
    Ping = 50,
    Pong = 51
}
