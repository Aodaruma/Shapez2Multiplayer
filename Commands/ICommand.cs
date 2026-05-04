using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Commands;

public interface ICommand
{
    CommandType Type { get; }

    uint LocalCommandId { get; }

    uint IssuerPlayerId { get; }

    void Serialize(PacketWriter writer);
}
