using System;
using Shapez2Multiplayer.Net;
using Steamworks;
using UnityEngine;

namespace Shapez2Multiplayer.UI;

public sealed class MultiplayerDebugUiBehaviour : MonoBehaviour
{
    private readonly Rect defaultRect = new(20f, 20f, 500f, 360f);
    private readonly int windowId = "Shapez2MultiplayerDebugUi".GetHashCode();

    private Core.Logging.ILogger? logger;
    private MultiplayerSessionController? session;
    private Rect windowRect;
    private string joinLobbyIdInput = string.Empty;
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
        GUILayout.Label($"Steam Initialized: {SteamClient.IsValid}");
        GUILayout.Label($"SteamId: {(SteamClient.IsValid ? ((ulong)SteamClient.SteamId).ToString() : "N/A")}");
        GUILayout.Label($"Status: {session.StatusText}");
        GUILayout.Label($"Lobby: {(session.IsInLobby ? session.CurrentLobbyId.ToString() : "Not joined")}");
        GUILayout.Label($"Role: {(session.IsHost ? "Host" : "Client/None")}");
        GUILayout.Label($"Owner: {(session.IsInLobby ? session.CurrentOwnerSteamId.ToString() : "N/A")}");
        GUILayout.Label($"Members: {session.CurrentMembers.Length}");
        GUILayout.Label($"Connected Peers: {session.ConnectedPeerCount}");
        GUILayout.Label($"RTT: {BuildRttText(session)}");

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

        GUILayout.Space(8);
        GUILayout.Label("Press F8 to hide/show this panel. Press F9 to join from clipboard.");
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
}
