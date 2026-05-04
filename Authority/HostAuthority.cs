using System;
using Shapez2Multiplayer.Commands;
using Shapez2Multiplayer.Net;
using Shapez2Multiplayer.Protocol;

namespace Shapez2Multiplayer.Authority;

public sealed class HostAuthority
{
    private readonly CommandSequencer sequencer;
    private readonly CommandValidator validator;
    private readonly WorldRevisionService worldRevision;
    private readonly INetworkTransport transport;
    private readonly PlayerRegistry playerRegistry;

    public HostAuthority(
        CommandSequencer sequencer,
        CommandValidator validator,
        WorldRevisionService worldRevision,
        INetworkTransport transport,
        PlayerRegistry playerRegistry)
    {
        this.sequencer = sequencer ?? throw new ArgumentNullException(nameof(sequencer));
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.worldRevision = worldRevision ?? throw new ArgumentNullException(nameof(worldRevision));
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.playerRegistry = playerRegistry ?? throw new ArgumentNullException(nameof(playerRegistry));
    }

    public void HandleTransportPacket(TransportPacket packet)
    {
        if (!PacketSerializer.TryDeserialize(packet.Payload, out PacketHeader header, out byte[] payload, out _))
        {
            return;
        }

        if (header.MessageType != MessageType.ClientCommand)
        {
            return;
        }

        ClientCommandMessage message = ClientCommandMessage.Deserialize(payload);
        OnClientCommand(packet.SenderPlayerId, message.Command);
    }

    public void OnClientCommand(uint playerId, ICommand command)
    {
        if (!RateLimitAllows(playerId))
        {
            Reject(playerId, command.LocalCommandId, CommandRejectReason.RateLimited);
            return;
        }

        CommandValidationResult result = validator.Validate(playerId, command);
        if (!result.Success)
        {
            Reject(playerId, command.LocalCommandId, result.Reason);
            return;
        }

        ulong globalSeq = sequencer.Next();
        ulong revision = worldRevision.Increment();

        AuthoritativeCommandMessage authoritative = new(globalSeq, revision, command);
        byte[] authoritativePacket = PacketSerializer.Serialize(
            new PacketHeader(
                protocolVersion: ProtocolLimits.ProtocolVersion,
                messageType: MessageType.AuthoritativeCommand,
                sessionId: 1,
                senderPlayerId: transport.LocalPlayerId,
                sequence: checked((uint)globalSeq),
                ackSequence: 0,
                worldRevision: revision),
            authoritative.Serialize());

        foreach (uint recipient in playerRegistry.GetAll())
        {
            _ = transport.Send(recipient, NetChannel.Commands, authoritativePacket, NetSendFlags.Reliable);
        }

        Ack(playerId, command.LocalCommandId, globalSeq, revision);
    }

    private void Ack(uint playerId, uint localCommandId, ulong globalSeq, ulong revision)
    {
        CommandAckMessage ack = new(localCommandId, globalSeq, revision);
        byte[] packet = PacketSerializer.Serialize(
            new PacketHeader(
                protocolVersion: ProtocolLimits.ProtocolVersion,
                messageType: MessageType.CommandAck,
                sessionId: 1,
                senderPlayerId: transport.LocalPlayerId,
                sequence: checked((uint)globalSeq),
                ackSequence: checked((uint)globalSeq),
                worldRevision: revision),
            ack.Serialize());

        _ = transport.Send(playerId, NetChannel.Control, packet, NetSendFlags.Reliable);
    }

    private void Reject(uint playerId, uint localCommandId, CommandRejectReason reason)
    {
        CommandRejectMessage reject = new(localCommandId, reason);
        byte[] packet = PacketSerializer.Serialize(
            new PacketHeader(
                protocolVersion: ProtocolLimits.ProtocolVersion,
                messageType: MessageType.CommandReject,
                sessionId: 1,
                senderPlayerId: transport.LocalPlayerId,
                sequence: 0,
                ackSequence: 0,
                worldRevision: worldRevision.Current),
            reject.Serialize());

        _ = transport.Send(playerId, NetChannel.Control, packet, NetSendFlags.Reliable);
    }

    private static bool RateLimitAllows(uint playerId)
    {
        _ = playerId;
        return true;
    }
}
