using System;
using Core.Logging;

namespace Shapez2Multiplayer;

public sealed class Shapez2MultiplayerMod : IMod
{
    private readonly ILogger logger;
    private bool disposed;

    public Shapez2MultiplayerMod(ILogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.logger.Info?.Log("[MP_INIT] Shapez2Multiplayer initialized version=0.1.0 protocol=1");
    }

    public void Dispose()
    {
        if (disposed)
        {
            logger.Warning?.Log("[MP_INIT] Shapez2Multiplayer already disposed");
            return;
        }

        disposed = true;
        logger.Info?.Log("[MP_INIT] Shapez2Multiplayer disposed");
    }
}
