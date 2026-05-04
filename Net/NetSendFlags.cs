using System;

namespace Shapez2Multiplayer.Net;

[Flags]
public enum NetSendFlags
{
    None = 0,
    Reliable = 1 << 0,
    Unreliable = 1 << 1
}
