namespace Shapez2Multiplayer.Sync;

public readonly struct WorldIslandState
{
    public WorldIslandState(
        string islandDefinitionId,
        int x,
        int y,
        int z,
        byte rotation)
    {
        IslandDefinitionId = islandDefinitionId;
        X = x;
        Y = y;
        Z = z;
        Rotation = rotation;
    }

    public string IslandDefinitionId { get; }

    public int X { get; }

    public int Y { get; }

    public int Z { get; }

    public byte Rotation { get; }
}
