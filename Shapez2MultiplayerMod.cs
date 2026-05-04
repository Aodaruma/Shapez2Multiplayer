using System;
using Shapez2Multiplayer.UI;
using UnityEngine;

namespace Shapez2Multiplayer;

public sealed class Shapez2MultiplayerMod : IMod
{
    private readonly Core.Logging.ILogger logger;
    private readonly GameObject uiRoot;
    private bool disposed;

    public Shapez2MultiplayerMod(Core.Logging.ILogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.logger.Info?.Log("[MP_INIT] Shapez2Multiplayer initialized version=0.1.0 protocol=1");

        uiRoot = new GameObject("Shapez2Multiplayer.DebugUi");
        UnityEngine.Object.DontDestroyOnLoad(uiRoot);
        MultiplayerDebugUiBehaviour ui = uiRoot.AddComponent<MultiplayerDebugUiBehaviour>();
        ui.Initialize(this.logger);
    }

    public void Dispose()
    {
        if (disposed)
        {
            logger.Warning?.Log("[MP_INIT] Shapez2Multiplayer already disposed");
            return;
        }

        disposed = true;
        UnityEngine.Object.Destroy(uiRoot);
        logger.Info?.Log("[MP_INIT] Shapez2Multiplayer disposed");
    }
}
