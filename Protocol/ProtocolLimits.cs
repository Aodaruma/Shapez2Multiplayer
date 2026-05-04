namespace Shapez2Multiplayer.Protocol;

public static class ProtocolLimits
{
    public const ushort ProtocolVersion = 1;
    public const int MaxPacketBytes = 1024 * 1024;
    public const int MaxUnreliablePayloadBytes = 1200;
    public const int MaxExtraPayloadBytes = 64 * 1024;
    public const int MaxBlueprintBytes = 4 * 1024 * 1024;
    public const int MaxSnapshotChunkBytes = 512 * 1024;
    public const int MaxChatBytes = 1024;
    public const int MaxCommandsPerSecondPerClient = 60;
    public const int MaxSnapshotBytes = 512 * 1024 * 1024;
}
