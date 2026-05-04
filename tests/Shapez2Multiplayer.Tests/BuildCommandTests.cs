using Shapez2Multiplayer.Commands;
using Shapez2Multiplayer.Protocol;

namespace Shapez2Multiplayer.Tests;

public class BuildCommandTests
{
    [Fact]
    public void BuildCommand_CanSerializeAndDeserialize()
    {
        BuildCommand command = new()
        {
            LocalCommandId = 44,
            IssuerPlayerId = 2,
            BuildingDefinitionId = "building.extractor",
            X = 10,
            Y = 20,
            Z = 3,
            Rotation = 1,
            Layer = 0,
            ExtraPayload = [10, 20, 30]
        };

        byte[] bytes = CommandSerializer.Serialize(command);
        ICommand actual = CommandSerializer.Deserialize(bytes);

        BuildCommand build = Assert.IsType<BuildCommand>(actual);
        Assert.Equal(command.LocalCommandId, build.LocalCommandId);
        Assert.Equal(command.IssuerPlayerId, build.IssuerPlayerId);
        Assert.Equal(command.BuildingDefinitionId, build.BuildingDefinitionId);
        Assert.Equal(command.X, build.X);
        Assert.Equal(command.Y, build.Y);
        Assert.Equal(command.Z, build.Z);
        Assert.Equal(command.Rotation, build.Rotation);
        Assert.Equal(command.Layer, build.Layer);
        Assert.Equal(command.ExtraPayload, build.ExtraPayload);
    }

    [Fact]
    public void ClientCommandMessage_CanWrapBuildCommand()
    {
        BuildCommand command = new()
        {
            LocalCommandId = 9,
            IssuerPlayerId = 3,
            BuildingDefinitionId = "building.belt",
            X = -1,
            Y = -2,
            Z = 0,
            Rotation = 2,
            Layer = 1
        };

        ClientCommandMessage message = new(command);
        byte[] bytes = message.Serialize();
        ClientCommandMessage actual = ClientCommandMessage.Deserialize(bytes);

        BuildCommand build = Assert.IsType<BuildCommand>(actual.Command);
        Assert.Equal(command.BuildingDefinitionId, build.BuildingDefinitionId);
        Assert.Equal(command.X, build.X);
        Assert.Equal(command.Y, build.Y);
    }
}
