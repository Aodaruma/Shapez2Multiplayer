using Shapez2Multiplayer.Authority;
using Shapez2Multiplayer.Commands;
using Shapez2Multiplayer.Net;
using Shapez2Multiplayer.Protocol;

namespace Shapez2Multiplayer.Tests;

public class HostAuthorityTests
{
    [Fact]
    public void HostAuthority_ConvertsClientCommandToAuthoritativeCommand()
    {
        using LoopbackTransport hostTransport = new(localPlayerId: 201);
        using LoopbackTransport clientTransport = new(localPlayerId: 202);

        PlayerRegistry playerRegistry = new();
        playerRegistry.Add(201);
        playerRegistry.Add(202);

        HostAuthority authority = new(
            new CommandSequencer(),
            new CommandValidator(),
            new WorldRevisionService(),
            hostTransport,
            playerRegistry);

        hostTransport.PacketReceived += authority.HandleTransportPacket;

        BuildCommand? receivedBuild = null;
        clientTransport.PacketReceived += packet =>
        {
            bool ok = PacketSerializer.TryDeserialize(packet.Payload, out PacketHeader header, out byte[] payload, out _);
            Assert.True(ok);
            if (header.MessageType != MessageType.AuthoritativeCommand)
            {
                return;
            }

            AuthoritativeCommandMessage message = AuthoritativeCommandMessage.Deserialize(payload);
            receivedBuild = Assert.IsType<BuildCommand>(message.Command);
        };

        hostTransport.Start();
        clientTransport.Start();

        BuildCommand build = new()
        {
            LocalCommandId = 77,
            IssuerPlayerId = 202,
            BuildingDefinitionId = "building.extractor",
            X = 30,
            Y = 40,
            Z = 1,
            Rotation = 2,
            Layer = 0
        };

        ClientCommandMessage clientMessage = new(build);
        byte[] packet = PacketSerializer.Serialize(
            new PacketHeader(
                protocolVersion: ProtocolLimits.ProtocolVersion,
                messageType: MessageType.ClientCommand,
                sessionId: 1,
                senderPlayerId: 202,
                sequence: 1,
                ackSequence: 0,
                worldRevision: 0),
            clientMessage.Serialize());

        bool sent = clientTransport.Send(
            recipientPlayerId: 201,
            channel: NetChannel.Commands,
            payload: packet,
            sendFlags: NetSendFlags.Reliable);
        Assert.True(sent);

        hostTransport.Pump();
        clientTransport.Pump();

        Assert.NotNull(receivedBuild);
        Assert.Equal(build.LocalCommandId, receivedBuild.LocalCommandId);
        Assert.Equal(build.BuildingDefinitionId, receivedBuild.BuildingDefinitionId);
        Assert.Equal(build.X, receivedBuild.X);
        Assert.Equal(build.Y, receivedBuild.Y);
    }
}
