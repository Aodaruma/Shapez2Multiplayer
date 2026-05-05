using System;
using Shapez2Multiplayer.Net;
using Steamworks;
using UnityEngine;

namespace Shapez2Multiplayer.UI;

public sealed class MultiplayerDebugUiBehaviour : MonoBehaviour
{
    private readonly Rect defaultRect = new(20f, 20f, 540f, 560f);
    private readonly int windowId = "Shapez2MultiplayerDebugUi".GetHashCode();

    private Core.Logging.ILogger? logger;
    private MultiplayerSessionController? session;
    private Rect windowRect;
    private Vector2 scrollPosition;
    private string joinLobbyIdInput = string.Empty;
    private string buildDefinitionInput = "building.extractor";
    private string xInput = "0";
    private string yInput = "0";
    private string zInput = "0";
    private string rotationInput = "0";
    private string layerInput = "0";
    private bool visible = true;
    private bool initialized;

    public void Initialize(Core.Logging.ILogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        session = new MultiplayerSessionController(logger, new FacepunchSteamPlatformApi());
        windowRect = defaultRect;
        initialized = true;
        this.logger.Info?.Log("[MP_UI] Debug UI initialized. Toggle with F8.");
    }

    private void Update()
    {
        if (!initialized || session is null)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.F8))
        {
            visible = !visible;
            logger?.Info?.Log($"[MP_UI] Visibility changed visible={visible}");
        }

        if (Input.GetKeyDown(KeyCode.F9))
        {
            joinLobbyIdInput = GUIUtility.systemCopyBuffer.Trim();
            if (session.TryJoinLobby(joinLobbyIdInput, out string msg))
            {
                logger?.Info?.Log("[MP_UI] Join requested from clipboard via F9");
            }
            else
            {
                logger?.Warning?.Log($"[MP_UI] Join from clipboard via F9 failed: {msg}");
            }
        }

        session.Tick();
    }

    private void OnGUI()
    {
        if (!initialized || session is null || !visible)
        {
            return;
        }

        windowRect = GUI.Window(windowId, windowRect, DrawWindow, "Shapez2Multiplayer Debug");
    }

    private void DrawWindow(int id)
    {
        if (session is null)
        {
            return;
        }

        GUILayout.BeginVertical();
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
        GUILayout.Label($"Steam Initialized: {SteamClient.IsValid}");
        GUILayout.Label($"SteamId: {(SteamClient.IsValid ? ((ulong)SteamClient.SteamId).ToString() : "N/A")}");
        GUILayout.Label($"Status: {session.StatusText}");
        GUILayout.Label($"Snapshot: {session.SnapshotStatusText}");
        GUILayout.Label($"Lobby: {(session.IsInLobby ? session.CurrentLobbyId.ToString() : "Not joined")}");
        GUILayout.Label($"Role: {(session.IsHost ? "Host" : "Client/None")}");
        GUILayout.Label($"Owner: {(session.IsInLobby ? session.CurrentOwnerSteamId.ToString() : "N/A")}");
        GUILayout.Label($"Members: {session.CurrentMembers.Length}");
        GUILayout.Label($"Connected Peers: {session.ConnectedPeerCount}");
        GUILayout.Label($"RTT: {BuildRttText(session)}");
        GUILayout.Label($"World Revision: {session.CurrentWorldRevision}");
        GUILayout.Label($"World Hash: {session.CurrentWorldHash}");
        GUILayout.Label($"World Entities: {session.WorldEntityCount}");
        GUILayout.Label($"Pending Commands: {session.PendingLocalCommandCount}");

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Host Lobby", GUILayout.Height(28)))
        {
            session.TryHostLobby(out _);
        }

        if (GUILayout.Button("Leave Lobby", GUILayout.Height(28)))
        {
            session.LeaveLobby();
        }
        GUILayout.EndHorizontal();

        if (session.IsInLobby && GUILayout.Button("Copy Lobby ID", GUILayout.Height(24)))
        {
            GUIUtility.systemCopyBuffer = session.CurrentLobbyId.ToString();
            logger?.Info?.Log($"[MP_UI] Copied lobby id={session.CurrentLobbyId}");
        }

        if (GUILayout.Button("Join From Clipboard", GUILayout.Height(24)))
        {
            joinLobbyIdInput = GUIUtility.systemCopyBuffer.Trim();
            session.TryJoinLobby(joinLobbyIdInput, out _);
        }

        GUILayout.Space(8);
        GUILayout.Label("Join Lobby ID:");
        joinLobbyIdInput = GUILayout.TextField(joinLobbyIdInput);
        if (GUILayout.Button("Join Lobby", GUILayout.Height(28)))
        {
            session.TryJoinLobby(joinLobbyIdInput, out _);
        }

        GUILayout.Space(12);
        GUILayout.Label("Build/Delete Command Test");
        GUILayout.BeginHorizontal();
        GUILayout.Label("Building ID", GUILayout.Width(90));
        buildDefinitionInput = GUILayout.TextField(buildDefinitionInput);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("X", GUILayout.Width(20));
        xInput = GUILayout.TextField(xInput, GUILayout.Width(64));
        GUILayout.Label("Y", GUILayout.Width(20));
        yInput = GUILayout.TextField(yInput, GUILayout.Width(64));
        GUILayout.Label("Z", GUILayout.Width(20));
        zInput = GUILayout.TextField(zInput, GUILayout.Width(64));
        GUILayout.Label("Rot", GUILayout.Width(30));
        rotationInput = GUILayout.TextField(rotationInput, GUILayout.Width(48));
        GUILayout.Label("Layer", GUILayout.Width(42));
        layerInput = GUILayout.TextField(layerInput, GUILayout.Width(48));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Send Build Cmd", GUILayout.Height(28)))
        {
            if (TryReadCommandInputs(out int x, out int y, out int z, out byte rotation, out byte layer, out string parseError))
            {
                session.TrySendBuildCommand(buildDefinitionInput, x, y, z, rotation, layer, out string result);
                logger?.Info?.Log($"[MP_UI] Send Build Cmd result={result}");
            }
            else
            {
                logger?.Warning?.Log($"[MP_UI] Build input parse failed: {parseError}");
            }
        }

        if (GUILayout.Button("Send Delete Cmd", GUILayout.Height(28)))
        {
            if (TryReadCommandInputs(out int x, out int y, out int z, out _, out byte layer, out string parseError))
            {
                session.TrySendDeleteCommand(x, y, z, layer, out string result);
                logger?.Info?.Log($"[MP_UI] Send Delete Cmd result={result}");
            }
            else
            {
                logger?.Warning?.Log($"[MP_UI] Delete input parse failed: {parseError}");
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.Label("Press F8 to hide/show this panel. Press F9 to join from clipboard.");
        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    private void OnDestroy()
    {
        session?.Dispose();
        session = null;
    }

    private static string BuildRttText(MultiplayerSessionController session)
    {
        if (session.PeerRttMs.Count == 0)
        {
            return "N/A";
        }

        System.Text.StringBuilder sb = new();
        bool first = true;
        foreach (System.Collections.Generic.KeyValuePair<ulong, int> kv in session.PeerRttMs)
        {
            if (!first)
            {
                sb.Append(" | ");
            }

            sb.Append(kv.Key);
            sb.Append(":");
            sb.Append(kv.Value);
            sb.Append("ms");
            first = false;
        }

        return sb.ToString();
    }

    private bool TryReadCommandInputs(
        out int x,
        out int y,
        out int z,
        out byte rotation,
        out byte layer,
        out string error)
    {
        if (!int.TryParse(xInput, out x))
        {
            y = 0;
            z = 0;
            rotation = 0;
            layer = 0;
            error = $"invalid X: {xInput}";
            return false;
        }

        if (!int.TryParse(yInput, out y))
        {
            z = 0;
            rotation = 0;
            layer = 0;
            error = $"invalid Y: {yInput}";
            return false;
        }

        if (!int.TryParse(zInput, out z))
        {
            rotation = 0;
            layer = 0;
            error = $"invalid Z: {zInput}";
            return false;
        }

        if (!byte.TryParse(rotationInput, out rotation))
        {
            layer = 0;
            error = $"invalid Rotation: {rotationInput}";
            return false;
        }

        if (!byte.TryParse(layerInput, out layer))
        {
            error = $"invalid Layer: {layerInput}";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
