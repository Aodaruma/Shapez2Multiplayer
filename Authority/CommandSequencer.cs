namespace Shapez2Multiplayer.Authority;

public sealed class CommandSequencer
{
    private ulong current;

    public ulong Next()
    {
        current++;
        return current;
    }
}
