namespace Shapez2Multiplayer.Net;

public enum NetChannel : int
{
    Control = 0,
    Commands = 1,
    Snapshot = 2,
    Ephemeral = 3,
    Chat = 4
}
