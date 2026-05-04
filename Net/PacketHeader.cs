using Shapez2Multiplayer.Protocol;

namespace Shapez2Multiplayer.Net;

public readonly struct PacketHeader
{
    public const uint MagicValue = 0x50324D53;
    public const int SizeInBytes = 32;

    public PacketHeader(
        ushort protocolVersion,
        MessageType messageType,
        uint sessionId,
        uint senderPlayerId,
        uint sequence,
        uint ackSequence,
        ulong worldRevision)
    {
        Magic = MagicValue;
        ProtocolVersion = protocolVersion;
        MessageType = messageType;
        SessionId = sessionId;
        SenderPlayerId = senderPlayerId;
        Sequence = sequence;
        AckSequence = ackSequence;
        WorldRevision = worldRevision;
    }

    public uint Magic { get; }

    public ushort ProtocolVersion { get; }

    public MessageType MessageType { get; }

    public uint SessionId { get; }

    public uint SenderPlayerId { get; }

    public uint Sequence { get; }

    public uint AckSequence { get; }

    public ulong WorldRevision { get; }

    public void Write(PacketWriter writer)
    {
        writer.WriteUInt32(Magic);
        writer.WriteUInt16(ProtocolVersion);
        writer.WriteUInt16((ushort)MessageType);
        writer.WriteUInt32(SessionId);
        writer.WriteUInt32(SenderPlayerId);
        writer.WriteUInt32(Sequence);
        writer.WriteUInt32(AckSequence);
        writer.WriteUInt64(WorldRevision);
    }

    public static PacketHeader Read(PacketReader reader)
    {
        uint magic = reader.ReadUInt32();
        ushort protocolVersion = reader.ReadUInt16();
        MessageType messageType = (MessageType)reader.ReadUInt16();
        uint sessionId = reader.ReadUInt32();
        uint senderPlayerId = reader.ReadUInt32();
        uint sequence = reader.ReadUInt32();
        uint ackSequence = reader.ReadUInt32();
        ulong worldRevision = reader.ReadUInt64();

        return new PacketHeader(
            magic,
            protocolVersion,
            messageType,
            sessionId,
            senderPlayerId,
            sequence,
            ackSequence,
            worldRevision);
    }

    private PacketHeader(
        uint magic,
        ushort protocolVersion,
        MessageType messageType,
        uint sessionId,
        uint senderPlayerId,
        uint sequence,
        uint ackSequence,
        ulong worldRevision)
    {
        Magic = magic;
        ProtocolVersion = protocolVersion;
        MessageType = messageType;
        SessionId = sessionId;
        SenderPlayerId = senderPlayerId;
        Sequence = sequence;
        AckSequence = ackSequence;
        WorldRevision = worldRevision;
    }
}
