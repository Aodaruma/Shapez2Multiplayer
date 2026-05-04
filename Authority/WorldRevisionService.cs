namespace Shapez2Multiplayer.Authority;

public sealed class WorldRevisionService
{
    public ulong Current { get; private set; }

    public ulong Increment()
    {
        Current++;
        return Current;
    }
}
