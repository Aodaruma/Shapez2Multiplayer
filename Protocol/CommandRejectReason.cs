namespace Shapez2Multiplayer.Protocol;

public enum CommandRejectReason : ushort
{
    Unknown = 0,
    RateLimited = 1,
    InvalidPayload = 2,
    UnknownBuilding = 3,
    PositionOutOfRange = 4,
    PlacementBlocked = 5,
    ResearchLocked = 6,
    PermissionDenied = 7,
    BlueprintTooLarge = 8,
    WorldRevisionTooOld = 9
}
