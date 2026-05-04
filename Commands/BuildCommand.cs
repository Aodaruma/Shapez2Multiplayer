using System;
using Shapez2Multiplayer.Net;
using Shapez2Multiplayer.Protocol;

namespace Shapez2Multiplayer.Commands;

public sealed class BuildCommand : ICommand
{
    private const int MaxBuildingDefinitionIdBytes = 1024;

    public CommandType Type => CommandType.Build;

    public uint LocalCommandId { get; set; }

    public uint IssuerPlayerId { get; set; }

    public string BuildingDefinitionId { get; set; } = string.Empty;

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public byte Rotation { get; set; }

    public byte Layer { get; set; }

    public byte[] ExtraPayload { get; set; } = Array.Empty<byte>();

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt16((ushort)Type);
        writer.WriteUInt32(LocalCommandId);
        writer.WriteUInt32(IssuerPlayerId);
        writer.WriteString(BuildingDefinitionId, MaxBuildingDefinitionIdBytes);
        writer.WriteInt32(X);
        writer.WriteInt32(Y);
        writer.WriteInt32(Z);
        writer.WriteByte(Rotation);
        writer.WriteByte(Layer);
        writer.WriteBytesWithLength(ExtraPayload, ProtocolLimits.MaxExtraPayloadBytes);
    }

    internal static BuildCommand DeserializeAfterType(PacketReader reader)
    {
        return new BuildCommand
        {
            LocalCommandId = reader.ReadUInt32(),
            IssuerPlayerId = reader.ReadUInt32(),
            BuildingDefinitionId = reader.ReadString(MaxBuildingDefinitionIdBytes),
            X = reader.ReadInt32(),
            Y = reader.ReadInt32(),
            Z = reader.ReadInt32(),
            Rotation = reader.ReadByte(),
            Layer = reader.ReadByte(),
            ExtraPayload = reader.ReadBytesWithLength(ProtocolLimits.MaxExtraPayloadBytes)
        };
    }
}
