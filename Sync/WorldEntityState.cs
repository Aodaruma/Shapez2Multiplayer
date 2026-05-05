namespace Shapez2Multiplayer.Sync;

public readonly struct WorldEntityState
{
    public WorldEntityState(
        string buildingDefinitionId,
        int x,
        int y,
        int z,
        byte rotation,
        byte layer)
    {
        BuildingDefinitionId = buildingDefinitionId;
        X = x;
        Y = y;
        Z = z;
        Rotation = rotation;
        Layer = layer;
    }

    public string BuildingDefinitionId { get; }

    public int X { get; }

    public int Y { get; }

    public int Z { get; }

    public byte Rotation { get; }

    public byte Layer { get; }
}
