using System.Collections.Generic;

namespace Shapez2Multiplayer.Authority;

public sealed class PlayerRegistry
{
    private readonly HashSet<uint> players = new();

    public void Add(uint playerId)
    {
        players.Add(playerId);
    }

    public void Remove(uint playerId)
    {
        players.Remove(playerId);
    }

    public IReadOnlyCollection<uint> GetAll()
    {
        return players;
    }
}
