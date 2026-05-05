using Shapez2Multiplayer.Commands;
using Shapez2Multiplayer.Sync;

namespace Shapez2Multiplayer.Tests;

public class WorldStateStoreTests
{
    [Fact]
    public void WorldStateStore_ApplyBuildAndDelete_UpdatesEntityCount()
    {
        WorldStateStore store = new();

        BuildCommand build = new()
        {
            LocalCommandId = 1,
            IssuerPlayerId = 2,
            BuildingDefinitionId = "building.extractor",
            X = 10,
            Y = 20,
            Z = 0,
            Rotation = 1,
            Layer = 0
        };

        bool appliedBuild = store.TryApplyCommand(build, out string buildError);
        Assert.True(appliedBuild, buildError);
        Assert.Equal(1, store.Count);

        DeleteCommand delete = new()
        {
            LocalCommandId = 2,
            IssuerPlayerId = 2,
            X = 10,
            Y = 20,
            Z = 0,
            Layer = 0
        };

        bool appliedDelete = store.TryApplyCommand(delete, out string deleteError);
        Assert.True(appliedDelete, deleteError);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void WorldStateStore_SnapshotRoundtrip_RestoresLayoutHash()
    {
        WorldStateStore source = new();

        CreateIslandCommand island = new()
        {
            LocalCommandId = 10,
            IssuerPlayerId = 2,
            IslandDefinitionId = "island.platform.space",
            X = 1,
            Y = 2,
            Z = 3,
            Rotation = 1
        };

        BuildCommand a = new()
        {
            LocalCommandId = 1,
            IssuerPlayerId = 2,
            BuildingDefinitionId = "building.extractor",
            X = 5,
            Y = 6,
            Z = 0,
            Rotation = 2,
            Layer = 1
        };

        BuildCommand b = new()
        {
            LocalCommandId = 2,
            IssuerPlayerId = 2,
            BuildingDefinitionId = "building.belt",
            X = 7,
            Y = 8,
            Z = 0,
            Rotation = 3,
            Layer = 1
        };

        Assert.True(source.TryApplyCommand(island, out _));
        Assert.True(source.TryApplyCommand(a, out _));
        Assert.True(source.TryApplyCommand(b, out _));

        ulong hashBefore = source.ComputeLayoutHash();
        byte[] snapshot = source.SerializeSnapshot();

        WorldStateStore target = new();
        target.LoadSnapshot(snapshot);

        Assert.Equal(source.Count, target.Count);
        Assert.Equal(hashBefore, target.ComputeLayoutHash());
    }

    [Fact]
    public void WorldStateStore_ApplyCreateAndDeleteIsland_UpdatesCount()
    {
        WorldStateStore store = new();

        CreateIslandCommand create = new()
        {
            LocalCommandId = 100,
            IssuerPlayerId = 7,
            IslandDefinitionId = "island.platform.space",
            X = 50,
            Y = 60,
            Z = 1,
            Rotation = 2
        };

        Assert.True(store.TryApplyCommand(create, out string createError), createError);
        Assert.Equal(1, store.Count);
        Assert.Equal(1, store.IslandCount);

        DeleteIslandCommand delete = new()
        {
            LocalCommandId = 101,
            IssuerPlayerId = 7,
            X = 50,
            Y = 60,
            Z = 1
        };

        Assert.True(store.TryApplyCommand(delete, out string deleteError), deleteError);
        Assert.Equal(0, store.Count);
        Assert.Equal(0, store.IslandCount);
    }
}
