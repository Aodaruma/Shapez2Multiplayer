using Shapez2Multiplayer.Commands;

namespace Shapez2Multiplayer.Tests;

public class IslandCommandTests
{
    [Fact]
    public void CreateIslandCommand_CanSerializeAndDeserialize()
    {
        CreateIslandCommand command = new()
        {
            LocalCommandId = 11,
            IssuerPlayerId = 3,
            IslandDefinitionId = "island.platform.space",
            X = 12,
            Y = -4,
            Z = 2,
            Rotation = 1
        };

        byte[] bytes = CommandSerializer.Serialize(command);
        ICommand actual = CommandSerializer.Deserialize(bytes);

        CreateIslandCommand create = Assert.IsType<CreateIslandCommand>(actual);
        Assert.Equal(command.LocalCommandId, create.LocalCommandId);
        Assert.Equal(command.IssuerPlayerId, create.IssuerPlayerId);
        Assert.Equal(command.IslandDefinitionId, create.IslandDefinitionId);
        Assert.Equal(command.X, create.X);
        Assert.Equal(command.Y, create.Y);
        Assert.Equal(command.Z, create.Z);
        Assert.Equal(command.Rotation, create.Rotation);
    }

    [Fact]
    public void DeleteIslandCommand_CanSerializeAndDeserialize()
    {
        DeleteIslandCommand command = new()
        {
            LocalCommandId = 22,
            IssuerPlayerId = 5,
            X = -30,
            Y = 44,
            Z = 3
        };

        byte[] bytes = CommandSerializer.Serialize(command);
        ICommand actual = CommandSerializer.Deserialize(bytes);

        DeleteIslandCommand delete = Assert.IsType<DeleteIslandCommand>(actual);
        Assert.Equal(command.LocalCommandId, delete.LocalCommandId);
        Assert.Equal(command.IssuerPlayerId, delete.IssuerPlayerId);
        Assert.Equal(command.X, delete.X);
        Assert.Equal(command.Y, delete.Y);
        Assert.Equal(command.Z, delete.Z);
    }
}
