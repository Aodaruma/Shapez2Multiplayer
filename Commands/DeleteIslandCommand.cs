using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Commands;

public sealed class DeleteIslandCommand : ICommand
{
    public CommandType Type => CommandType.DeleteIsland;

    public uint LocalCommandId { get; set; }

    public uint IssuerPlayerId { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt16((ushort)Type);
        writer.WriteUInt32(LocalCommandId);
        writer.WriteUInt32(IssuerPlayerId);
        writer.WriteInt32(X);
        writer.WriteInt32(Y);
        writer.WriteInt32(Z);
    }

    internal static DeleteIslandCommand DeserializeAfterType(PacketReader reader)
    {
        return new DeleteIslandCommand
        {
            LocalCommandId = reader.ReadUInt32(),
            IssuerPlayerId = reader.ReadUInt32(),
            X = reader.ReadInt32(),
            Y = reader.ReadInt32(),
            Z = reader.ReadInt32()
        };
    }
}
