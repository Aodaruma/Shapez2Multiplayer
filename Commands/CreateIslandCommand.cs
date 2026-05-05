using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Commands;

public sealed class CreateIslandCommand : ICommand
{
    private const int MaxIslandDefinitionIdBytes = 1024;

    public CommandType Type => CommandType.CreateIsland;

    public uint LocalCommandId { get; set; }

    public uint IssuerPlayerId { get; set; }

    public string IslandDefinitionId { get; set; } = string.Empty;

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public byte Rotation { get; set; }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt16((ushort)Type);
        writer.WriteUInt32(LocalCommandId);
        writer.WriteUInt32(IssuerPlayerId);
        writer.WriteString(IslandDefinitionId, MaxIslandDefinitionIdBytes);
        writer.WriteInt32(X);
        writer.WriteInt32(Y);
        writer.WriteInt32(Z);
        writer.WriteByte(Rotation);
    }

    internal static CreateIslandCommand DeserializeAfterType(PacketReader reader)
    {
        return new CreateIslandCommand
        {
            LocalCommandId = reader.ReadUInt32(),
            IssuerPlayerId = reader.ReadUInt32(),
            IslandDefinitionId = reader.ReadString(MaxIslandDefinitionIdBytes),
            X = reader.ReadInt32(),
            Y = reader.ReadInt32(),
            Z = reader.ReadInt32(),
            Rotation = reader.ReadByte()
        };
    }
}
