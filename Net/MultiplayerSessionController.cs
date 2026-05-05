using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Core.Logging;
using Shapez2Multiplayer.Authority;
using Shapez2Multiplayer.Commands;
using Shapez2Multiplayer.Protocol;
using Shapez2Multiplayer.Sync;
using Steamworks;
using Steamworks.Data;

namespace Shapez2Multiplayer.Net;

public sealed class MultiplayerSessionController : IDisposable
{
    private const int SnapshotChunkBytes = 32 * 1024;

    private readonly ILogger logger;
    private readonly ISteamPlatformApi steamApi;
    private readonly SteamLobbyService lobbyService;
    private readonly CommandValidator commandValidator = new();
    private readonly WorldStateStore worldState = new();
    private readonly HashSet<ulong> connectedPeers = new();
    private readonly HashSet<ulong> snapshotSentPeers = new();
    private readonly Dictionary<uint, PendingPing> pendingPings = new();
    private readonly Dictionary<uint, PendingLocalCommand> pendingLocalCommands = new();
    private readonly Dictionary<ulong, int> peerRtts = new();
    private readonly Dictionary<ulong, SnapshotReceiveBuffer> snapshotBuffers = new();
    private readonly HashSet<BuildingId> observedBuildingIds = new();
    private readonly HashSet<IslandId> observedIslandIds = new();
    private readonly Dictionary<string, IBuildingDefinition> knownBuildingDefinitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IIslandDefinition> knownIslandDefinitions = new(StringComparer.Ordinal);

    private bool disposed;
    private uint nextSequence;
    private uint nextPingId = 1;
    private uint nextLocalCommandId = 1;
    private uint nextSnapshotId = 1;
    private ulong nextGlobalSequence;
    private ulong localWorldRevision;
    private long nextPingAtMs;
    private long nextStatusLogAtMs;
    private string lastCommandSummary = "N/A";
    private bool mapHooksReady;
    private bool suppressMapEventCommands = false;
    private Player? trackedLocalPlayer;
    private IMapModel? trackedMap;
    private long mapEventSuppressionUntilMs;
    private bool worldSyncEnabled;
    private bool pendingAutoLeaveOnNullMap;
    private long autoLeaveOnNullMapAtMs;
    private const int AutoLeaveNullMapDelayMs = 1500;
    private bool pendingShadowSnapshotApply;
    private int pendingShadowSnapshotApplyAttempts;
    private long nextShadowSnapshotApplyAtMs;
    private string pendingShadowSnapshotApplyReason = string.Empty;
    private const int ShadowSnapshotApplyRetryDelayMs = 250;
    private const int ShadowSnapshotApplyWarnEveryAttempts = 20;
    private const int ShadowSnapshotApplyBusyDelayMs = 200;

    public MultiplayerSessionController(ILogger logger, ISteamPlatformApi steamApi)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.steamApi = steamApi ?? throw new ArgumentNullException(nameof(steamApi));
        lobbyService = new SteamLobbyService(steamApi);
        StatusText = "Idle";
        SnapshotStatusText = "Idle";
        SteamNetworking.OnP2PSessionRequest += HandleP2PSessionRequest;
        SteamNetworking.OnP2PConnectionFailed += HandleP2PConnectionFailed;
        SteamMatchmaking.OnLobbyMemberJoined += HandleLobbyMemberJoined;
    }

    public bool IsInLobby { get; private set; }

    public bool IsHost { get; private set; }

    public ulong CurrentLobbyId { get; private set; }

    public string StatusText { get; private set; }

    public string SnapshotStatusText { get; private set; }

    public ulong[] CurrentMembers => IsInLobby ? steamApi.GetLobbyMemberSteamIds(CurrentLobbyId) : Array.Empty<ulong>();

    public ulong CurrentOwnerSteamId => IsInLobby ? steamApi.GetLobbyOwnerSteamId(CurrentLobbyId) : 0;

    public int ConnectedPeerCount => connectedPeers.Count;

    public IReadOnlyDictionary<ulong, int> PeerRttMs => peerRtts;

    public ulong CurrentWorldRevision => localWorldRevision;

    public ulong CurrentWorldHash => worldState.ComputeLayoutHash();

    public int WorldEntityCount => worldState.Count;

    public int PendingLocalCommandCount => pendingLocalCommands.Count;

    public string LastCommandSummary => lastCommandSummary;

    public bool WorldSyncEnabled => worldSyncEnabled;

    public bool TryHostLobby(out string message)
    {
        if (IsInLobby)
        {
            message = "Host skipped: already in lobby (leave first)";
            StatusText = message;
            logger.Info?.Log($"[MP_LOBBY] {message} lobby={CurrentLobbyId}");
            return false;
        }

        LobbyOperationResult result = lobbyService.HostLobby();
        if (!result.Success)
        {
            message = $"Host failed: {result.Error}";
            StatusText = message;
            logger.Warning?.Log($"[MP_LOBBY] {message}");
            return false;
        }

        InitializeLobbyState(isHost: true, result.LobbyId);
        message = $"Hosting lobby: {CurrentLobbyId}";
        StatusText = message;
        logger.Info?.Log($"[MP_LOBBY] {message}");
        return true;
    }

    public bool TryJoinLobby(string lobbyIdText, out string message)
    {
        if (IsInLobby)
        {
            message = "Join skipped: already in lobby (leave first)";
            StatusText = message;
            logger.Info?.Log($"[MP_LOBBY] {message} lobby={CurrentLobbyId}");
            return false;
        }

        if (!ulong.TryParse(lobbyIdText, out ulong lobbyId))
        {
            message = "Join failed: invalid lobby id";
            StatusText = message;
            logger.Warning?.Log($"[MP_LOBBY] {message} input={lobbyIdText}");
            return false;
        }

        LobbyOperationResult result = lobbyService.JoinLobby(lobbyId);
        if (!result.Success)
        {
            message = $"Join failed: {result.Error}";
            StatusText = message;
            logger.Warning?.Log($"[MP_LOBBY] {message} lobby={lobbyId}");
            return false;
        }

        InitializeLobbyState(isHost: false, result.LobbyId);
        SendHelloToKnownMembers();
        message = $"Joined lobby: {CurrentLobbyId}";
        StatusText = message;
        logger.Info?.Log($"[MP_LOBBY] {message}");
        return true;
    }

    public bool TrySendBuildCommand(string buildingDefinitionId, int x, int y, int z, byte rotation, byte layer, out string message)
    {
        return TrySendBuildCommandInternal(buildingDefinitionId, x, y, z, rotation, layer, fromMapHook: false, out message);
    }

    public bool TryEnableWorldSync(out string message)
    {
        if (!IsInLobby)
        {
            message = "World sync failed: not in lobby";
            return false;
        }

        if (IsHost)
        {
            worldSyncEnabled = true;
            if (TryRebuildShadowWorldFromLiveMap(out string hostSyncError))
            {
                message = "Host world sync refreshed from live map";
                return true;
            }

            message = $"Host world sync enabled, but refresh failed: {hostSyncError}";
            return false;
        }

        worldSyncEnabled = true;
        if (worldState.Count > 0)
        {
            QueueShadowSnapshotApply("enable-world-sync");
        }

        ulong hostSteamId = CurrentOwnerSteamId;
        if (hostSteamId != 0)
        {
            SendHello(hostSteamId);
        }

        message = "World sync enabled; snapshot apply queued and host refresh requested";
        return true;
    }

    public void DisableWorldSync()
    {
        worldSyncEnabled = false;
        pendingShadowSnapshotApply = false;
        pendingShadowSnapshotApplyAttempts = 0;
        nextShadowSnapshotApplyAtMs = 0;
        pendingShadowSnapshotApplyReason = string.Empty;
    }

    private bool TrySendBuildCommandInternal(string buildingDefinitionId, int x, int y, int z, byte rotation, byte layer, bool fromMapHook, out string message)
    {
        if (string.IsNullOrWhiteSpace(buildingDefinitionId))
        {
            message = "Build failed: building id is empty";
            logger.Warning?.Log($"[MP_COMMAND] {message}");
            return false;
        }

        BuildCommand command = new()
        {
            LocalCommandId = nextLocalCommandId++,
            IssuerPlayerId = GetLocalPlayerId(),
            BuildingDefinitionId = buildingDefinitionId.Trim(),
            X = x,
            Y = y,
            Z = z,
            Rotation = rotation,
            Layer = layer,
            ExtraPayload = Array.Empty<byte>()
        };

        return TrySendCommand(command, fromMapHook, out message);
    }

    public bool TrySendDeleteCommand(int x, int y, int z, byte layer, out string message)
    {
        return TrySendDeleteCommandInternal(x, y, z, layer, fromMapHook: false, out message);
    }

    private bool TrySendDeleteCommandInternal(int x, int y, int z, byte layer, bool fromMapHook, out string message)
    {
        DeleteCommand command = new()
        {
            LocalCommandId = nextLocalCommandId++,
            IssuerPlayerId = GetLocalPlayerId(),
            X = x,
            Y = y,
            Z = z,
            Layer = layer
        };

        return TrySendCommand(command, fromMapHook, out message);
    }

    private bool TrySendCreateIslandCommandInternal(string islandDefinitionId, int x, int y, int z, byte rotation, bool fromMapHook, out string message)
    {
        if (string.IsNullOrWhiteSpace(islandDefinitionId))
        {
            message = "CreateIsland failed: island id is empty";
            logger.Warning?.Log($"[MP_COMMAND] {message}");
            return false;
        }

        CreateIslandCommand command = new()
        {
            LocalCommandId = nextLocalCommandId++,
            IssuerPlayerId = GetLocalPlayerId(),
            IslandDefinitionId = islandDefinitionId.Trim(),
            X = x,
            Y = y,
            Z = z,
            Rotation = rotation
        };

        return TrySendCommand(command, fromMapHook, out message);
    }

    private bool TrySendDeleteIslandCommandInternal(int x, int y, int z, bool fromMapHook, out string message)
    {
        DeleteIslandCommand command = new()
        {
            LocalCommandId = nextLocalCommandId++,
            IssuerPlayerId = GetLocalPlayerId(),
            X = x,
            Y = y,
            Z = z
        };

        return TrySendCommand(command, fromMapHook, out message);
    }

    public void LeaveLobby()
    {
        if (!IsInLobby)
        {
            return;
        }

        NetworkError error = lobbyService.LeaveLobby(CurrentLobbyId);
        logger.Info?.Log($"[MP_LOBBY] Leave lobby={CurrentLobbyId} result={error}");

        IsInLobby = false;
        IsHost = false;
        CurrentLobbyId = 0;
        nextStatusLogAtMs = 0;
        StatusText = "Idle";
        SnapshotStatusText = "Idle";

        ResetTransientState();
        worldState.Clear();
        localWorldRevision = 0;
        nextGlobalSequence = 0;
        lastCommandSummary = "N/A";
        worldSyncEnabled = false;
        pendingAutoLeaveOnNullMap = false;
        autoLeaveOnNullMapAtMs = 0;
        pendingShadowSnapshotApply = false;
        pendingShadowSnapshotApplyAttempts = 0;
        nextShadowSnapshotApplyAtMs = 0;
        pendingShadowSnapshotApplyReason = string.Empty;
        UnbindMapHooks();
    }

    public void Tick()
    {
        if (steamApi.IsInitialized)
        {
            SteamClient.RunCallbacks();
        }

        if (!IsInLobby)
        {
            return;
        }

        UpdateMapHooks();
        ProcessPendingAutoLeaveOnNullMap();

        PumpIncomingPackets();
        ProcessPendingShadowSnapshotApply();
        SendPeriodicPings();
        StatusText = $"Lobby={CurrentLobbyId} host={IsHost} peers={ConnectedPeerCount}";
        EmitStatusLogIfDue();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        SteamNetworking.OnP2PSessionRequest -= HandleP2PSessionRequest;
        SteamNetworking.OnP2PConnectionFailed -= HandleP2PConnectionFailed;
        SteamMatchmaking.OnLobbyMemberJoined -= HandleLobbyMemberJoined;
        UnbindMapHooks();
        LeaveLobby();
    }

    private void InitializeLobbyState(bool isHost, ulong lobbyId)
    {
        IsInLobby = true;
        IsHost = isHost;
        CurrentLobbyId = lobbyId;
        nextPingAtMs = GetNowMs() + 1000;
        nextStatusLogAtMs = GetNowMs() + 2000;

        ResetTransientState();
        worldState.Clear();
        localWorldRevision = 0;
        nextGlobalSequence = 0;
        lastCommandSummary = "N/A";
        worldSyncEnabled = isHost;
        pendingAutoLeaveOnNullMap = false;
        autoLeaveOnNullMapAtMs = 0;
        pendingShadowSnapshotApply = false;
        pendingShadowSnapshotApplyAttempts = 0;
        nextShadowSnapshotApplyAtMs = 0;
        pendingShadowSnapshotApplyReason = string.Empty;
        mapHooksReady = false;
        observedBuildingIds.Clear();
        observedIslandIds.Clear();
        knownBuildingDefinitions.Clear();
        knownIslandDefinitions.Clear();

        if (isHost)
        {
            SnapshotStatusText = "Host world initialized (empty)";
        }
        else
        {
            SnapshotStatusText = "Waiting snapshot from host";
        }
    }

    private void ResetTransientState()
    {
        connectedPeers.Clear();
        peerRtts.Clear();
        pendingPings.Clear();
        pendingLocalCommands.Clear();
        snapshotBuffers.Clear();
        snapshotSentPeers.Clear();
        pendingAutoLeaveOnNullMap = false;
        autoLeaveOnNullMapAtMs = 0;
    }

    private void UpdateMapHooks()
    {
        if (!IsInLobby || !StaticGameCoreAccessor.HasInstance())
        {
            return;
        }

        Player? localPlayer = StaticGameCoreAccessor.G?.LocalPlayer;
        if (localPlayer is null)
        {
            return;
        }

        if (trackedLocalPlayer != localPlayer)
        {
            if (trackedLocalPlayer is not null)
            {
                trackedLocalPlayer.OnMapChanged.TryUnregister(HandleLocalPlayerMapChanged);
            }

            trackedLocalPlayer = localPlayer;
            trackedLocalPlayer.OnMapChanged.Register(HandleLocalPlayerMapChanged);
            mapHooksReady = false;
            observedBuildingIds.Clear();
            logger.Info?.Log("[MP_HOOK] Attached LocalPlayer.OnMapChanged");
        }

        IMapModel currentMap = localPlayer.CurrentMap;
        if (currentMap != default && !ReferenceEquals(currentMap, trackedMap))
        {
            HandleLocalPlayerMapChanged(currentMap);
        }
        else if (currentMap == default && trackedMap != default)
        {
            HandleLocalPlayerMapChanged(default);
        }
    }

    private void HandleLocalPlayerMapChanged(IMapModel? map)
    {
        UnbindTrackedMapEvents();
        trackedMap = map;
        observedBuildingIds.Clear();
        observedIslandIds.Clear();

        if (trackedMap == default)
        {
            mapHooksReady = false;
            if (IsInLobby)
            {
                pendingAutoLeaveOnNullMap = true;
                autoLeaveOnNullMapAtMs = GetNowMs() + AutoLeaveNullMapDelayMs;
                logger.Info?.Log($"[MP_HOOK] MapChanged: null map (auto leave scheduled in {AutoLeaveNullMapDelayMs}ms)");
            }
            else
            {
                logger.Info?.Log("[MP_HOOK] MapChanged: null map");
            }
            return;
        }

        pendingAutoLeaveOnNullMap = false;
        autoLeaveOnNullMapAtMs = 0;

        trackedMap.OnBuildingAdded.Register(HandleMapBuildingAdded);
        trackedMap.OnBeforeBuildingRemoved.Register(HandleMapBeforeBuildingRemoved);
        trackedMap.OnIslandAdded.Register(HandleMapIslandAdded);
        trackedMap.OnBeforeIslandRemoved.Register(HandleMapBeforeIslandRemoved);

        foreach (IslandModel island in trackedMap.Islands)
        {
            observedIslandIds.Add(island.Id);
            CacheKnownIslandDefinition(island.Definition);
        }

        foreach (BuildingModel building in trackedMap.Buildings)
        {
            observedBuildingIds.Add(building.Id);
            CacheKnownBuildingDefinition(building.Definition);
        }

        mapHooksReady = true;
        mapEventSuppressionUntilMs = GetNowMs() + 2000;
        logger.Info?.Log($"[MP_HOOK] Map hooks ready. existingIslands={observedIslandIds.Count} existingBuildings={observedBuildingIds.Count}");

        if (IsHost && worldSyncEnabled)
        {
            if (TryRebuildShadowWorldFromLiveMap(out string error))
            {
                logger.Info?.Log($"[MP_HOOK] Host shadow world refreshed from live map entities={worldState.Count} revision={localWorldRevision}");
            }
            else
            {
                logger.Warning?.Log($"[MP_HOOK] Host shadow world refresh failed: {error}");
            }
        }
    }

    private void UnbindTrackedMapEvents()
    {
        if (trackedMap == default)
        {
            return;
        }

        trackedMap.OnBuildingAdded.TryUnregister(HandleMapBuildingAdded);
        trackedMap.OnBeforeBuildingRemoved.TryUnregister(HandleMapBeforeBuildingRemoved);
        trackedMap.OnIslandAdded.TryUnregister(HandleMapIslandAdded);
        trackedMap.OnBeforeIslandRemoved.TryUnregister(HandleMapBeforeIslandRemoved);
        trackedMap = default;
    }

    private void UnbindMapHooks()
    {
        UnbindTrackedMapEvents();
        if (trackedLocalPlayer is not null)
        {
            trackedLocalPlayer.OnMapChanged.TryUnregister(HandleLocalPlayerMapChanged);
            trackedLocalPlayer = null;
        }

        mapHooksReady = false;
        observedBuildingIds.Clear();
        knownBuildingDefinitions.Clear();
        pendingAutoLeaveOnNullMap = false;
        autoLeaveOnNullMapAtMs = 0;
    }

    private bool TrySendCommand(ICommand command, bool fromMapHook, out string message)
    {
        if (!IsInLobby)
        {
            message = "Command send failed: not in lobby";
            logger.Warning?.Log($"[MP_COMMAND] {message}");
            return false;
        }

        if (IsHost)
        {
            if (!TryAcceptAndBroadcastCommand(GetLocalPlayerId(), GetLocalSteamId(), command, out string hostError))
            {
                message = $"Host command failed: {hostError}";
                logger.Warning?.Log($"[MP_COMMAND] {message}");
                return false;
            }

            if (!fromMapHook && worldSyncEnabled)
            {
                if (!TryApplyCommandToRealMap(command, out string applyError))
                {
                    logger.Warning?.Log($"[MP_COMMAND] Host local command accepted but real map apply failed: {applyError}");
                }
            }

            message = $"Host applied local {command.Type} localId={command.LocalCommandId}";
            logger.Info?.Log($"[MP_COMMAND] {message}");
            return true;
        }

        ulong hostSteamId = CurrentOwnerSteamId;
        if (hostSteamId == 0)
        {
            message = "Command send failed: host not available";
            logger.Warning?.Log($"[MP_COMMAND] {message}");
            return false;
        }

        ClientCommandMessage request = new(command);
        bool sent = SendPacket(
            hostSteamId,
            NetChannel.Commands,
            MessageType.ClientCommand,
            request.Serialize(),
            P2PSend.Reliable);

        if (!sent)
        {
            message = "Command send failed: transport send error";
            logger.Warning?.Log($"[MP_COMMAND] {message} type={command.Type} localId={command.LocalCommandId}");
            return false;
        }

        pendingLocalCommands[command.LocalCommandId] = new PendingLocalCommand(command, GetNowMs());
        message = $"Sent {command.Type} to host localId={command.LocalCommandId}";
        logger.Info?.Log($"[MP_COMMAND] {message}");
        return true;
    }

    private static ulong GetLocalSteamId()
    {
        return SteamClient.IsValid ? SteamClient.SteamId : 0;
    }

    private static uint GetLocalPlayerId()
    {
        return SteamClient.IsValid ? SteamClient.SteamId.AccountId : 0;
    }

    private void HandleP2PSessionRequest(SteamId requester)
    {
        SteamNetworking.AcceptP2PSessionWithUser(requester);
        logger.Info?.Log($"[MP_NET] Accepted P2P session request from {requester}");
    }

    private void HandleP2PConnectionFailed(SteamId remote, P2PSessionError error)
    {
        logger.Warning?.Log($"[MP_NET] P2P connection failed remote={remote} error={error}");
    }

    private void HandleLobbyMemberJoined(Lobby lobby, Friend member)
    {
        if (!IsInLobby || (ulong)lobby.Id != CurrentLobbyId)
        {
            return;
        }

        ulong memberSteamId = member.Id;
        if (memberSteamId == 0 || memberSteamId == GetLocalSteamId())
        {
            return;
        }

        if (IsHost)
        {
            SendWelcome(memberSteamId);
            SendSnapshotToPeer(memberSteamId);
        }
    }

    private void SendHelloToKnownMembers()
    {
        ulong localSteamId = GetLocalSteamId();
        foreach (ulong memberSteamId in CurrentMembers)
        {
            if (memberSteamId == 0 || memberSteamId == localSteamId)
            {
                continue;
            }

            SendHello(memberSteamId);
        }
    }

    private void SendHello(ulong recipientSteamId)
    {
        HelloMessage hello = new(GetLocalSteamId());
        if (SendPacket(recipientSteamId, NetChannel.Control, MessageType.Hello, hello.Serialize(), P2PSend.Reliable))
        {
            logger.Info?.Log($"[MP_NET] Sent Hello to={recipientSteamId}");
        }
    }

    private void SendWelcome(ulong recipientSteamId)
    {
        WelcomeMessage welcome = new(GetLocalSteamId());
        if (SendPacket(recipientSteamId, NetChannel.Control, MessageType.Welcome, welcome.Serialize(), P2PSend.Reliable))
        {
            logger.Info?.Log($"[MP_NET] Sent Welcome to={recipientSteamId}");
        }
    }

    private void SendPing(ulong recipientSteamId, uint pingId)
    {
        PingMessage ping = new(pingId);
        _ = SendPacket(recipientSteamId, NetChannel.Control, MessageType.Ping, ping.Serialize(), P2PSend.Reliable);
    }

    private void SendPong(ulong recipientSteamId, uint pingId)
    {
        PongMessage pong = new(pingId);
        _ = SendPacket(recipientSteamId, NetChannel.Control, MessageType.Pong, pong.Serialize(), P2PSend.Reliable);
    }

    private bool SendPacket(ulong recipientSteamId, NetChannel channel, MessageType type, byte[] payload, P2PSend sendType)
    {
        uint sessionId = unchecked((uint)CurrentLobbyId);
        PacketHeader header = new(
            protocolVersion: ProtocolLimits.ProtocolVersion,
            messageType: type,
            sessionId: sessionId,
            senderPlayerId: GetLocalPlayerId(),
            sequence: ++nextSequence,
            ackSequence: 0,
            worldRevision: localWorldRevision);

        byte[] packetBytes = PacketSerializer.Serialize(header, payload);
        bool sent = SteamNetworking.SendP2PPacket((SteamId)recipientSteamId, packetBytes, packetBytes.Length, (int)channel, sendType);

        if (!sent)
        {
            logger.Warning?.Log($"[MP_NET] Failed to send packet type={type} channel={channel} to={recipientSteamId}");
        }

        return sent;
    }

    private void PumpIncomingPackets()
    {
        PumpIncomingChannel(NetChannel.Control);
        PumpIncomingChannel(NetChannel.Commands);
        PumpIncomingChannel(NetChannel.Snapshot);
    }

    private void PumpIncomingChannel(NetChannel channel)
    {
        while (SteamNetworking.IsP2PPacketAvailable(channel: (int)channel))
        {
            P2Packet? packet = SteamNetworking.ReadP2PPacket(channel: (int)channel);
            if (!packet.HasValue)
            {
                break;
            }

            ulong senderSteamId = packet.Value.SteamId;
            SteamNetworking.AcceptP2PSessionWithUser((SteamId)senderSteamId);

            if (!PacketSerializer.TryDeserialize(packet.Value.Data, out PacketHeader header, out byte[] payload, out NetworkError error))
            {
                logger.Warning?.Log($"[MP_NET] Drop packet deserialize error={error} from={senderSteamId}");
                continue;
            }

            HandleInboundMessage(senderSteamId, channel, header, payload);
        }
    }

    private void HandleInboundMessage(ulong senderSteamId, NetChannel channel, PacketHeader header, byte[] payload)
    {
        _ = channel;

        try
        {
            switch (header.MessageType)
            {
                case MessageType.Hello:
                {
                    _ = HelloMessage.Deserialize(payload);
                    TrackConnectedPeer(senderSteamId);
                    logger.Info?.Log($"[MP_NET] Received Hello from={senderSteamId}");
                    if (IsHost)
                    {
                        SendWelcome(senderSteamId);
                        SendSnapshotToPeer(senderSteamId, force: true);
                    }

                    break;
                }

                case MessageType.Welcome:
                {
                    _ = WelcomeMessage.Deserialize(payload);
                    TrackConnectedPeer(senderSteamId);
                    logger.Info?.Log($"[MP_NET] Received Welcome from={senderSteamId}");
                    break;
                }

                case MessageType.Ping:
                {
                    PingMessage ping = PingMessage.Deserialize(payload);
                    TrackConnectedPeer(senderSteamId);
                    SendPong(senderSteamId, ping.PingId);
                    break;
                }

                case MessageType.Pong:
                {
                    PongMessage pong = PongMessage.Deserialize(payload);
                    TrackConnectedPeer(senderSteamId);
                    HandlePong(senderSteamId, pong);
                    break;
                }

                case MessageType.ClientCommand:
                {
                    if (!IsHost)
                    {
                        logger.Warning?.Log($"[MP_COMMAND] ClientCommand ignored on non-host from={senderSteamId}");
                        break;
                    }

                    ClientCommandMessage message = ClientCommandMessage.Deserialize(payload);
                    uint senderPlayerId = header.SenderPlayerId != 0 ? header.SenderPlayerId : unchecked((uint)senderSteamId);
                    if (!TryAcceptAndBroadcastCommand(senderPlayerId, senderSteamId, message.Command, out string error))
                    {
                        logger.Warning?.Log($"[MP_COMMAND] Reject incoming command from={senderSteamId} reason={error}");
                    }
                    else if (worldSyncEnabled)
                    {
                        if (!TryApplyCommandToRealMap(message.Command, out string applyError))
                        {
                            logger.Warning?.Log($"[MP_COMMAND] Incoming command accepted but real map apply failed from={senderSteamId} reason={applyError}");
                        }
                    }

                    break;
                }

                case MessageType.AuthoritativeCommand:
                {
                    AuthoritativeCommandMessage message = AuthoritativeCommandMessage.Deserialize(payload);
                    ApplyAuthoritativeCommand(senderSteamId, message);
                    break;
                }

                case MessageType.CommandAck:
                {
                    CommandAckMessage ack = CommandAckMessage.Deserialize(payload);
                    HandleCommandAck(senderSteamId, ack);
                    break;
                }

                case MessageType.CommandReject:
                {
                    CommandRejectMessage reject = CommandRejectMessage.Deserialize(payload);
                    HandleCommandReject(senderSteamId, reject);
                    break;
                }

                case MessageType.SnapshotBegin:
                {
                    SnapshotBeginMessage begin = SnapshotBeginMessage.Deserialize(payload);
                    HandleSnapshotBegin(senderSteamId, begin);
                    break;
                }

                case MessageType.SnapshotChunk:
                {
                    SnapshotChunkMessage chunk = SnapshotChunkMessage.Deserialize(payload);
                    HandleSnapshotChunk(senderSteamId, chunk);
                    break;
                }

                case MessageType.SnapshotEnd:
                {
                    SnapshotEndMessage end = SnapshotEndMessage.Deserialize(payload);
                    HandleSnapshotEnd(senderSteamId, end);
                    break;
                }

                case MessageType.WorldHash:
                {
                    WorldHashMessage worldHash = WorldHashMessage.Deserialize(payload);
                    HandleWorldHash(senderSteamId, worldHash);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.Warning?.Log($"[MP_NET] Message handling failed type={header.MessageType} from={senderSteamId} error={ex.Message}");
        }
    }

    private void HandlePong(ulong senderSteamId, PongMessage pong)
    {
        if (pendingPings.TryGetValue(pong.PingId, out PendingPing pending) && pending.PeerSteamId == senderSteamId)
        {
            pendingPings.Remove(pong.PingId);
            int rtt = checked((int)(GetNowMs() - pending.SentAtMs));
            peerRtts[senderSteamId] = rtt;
            logger.Info?.Log($"[MP_NET] RTT peer={senderSteamId} rttMs={rtt}");
        }
    }

    private void HandleMapBuildingAdded(BuildingModel building)
    {
        CacheKnownBuildingDefinition(building.Definition);

        if (!mapHooksReady || suppressMapEventCommands || !IsInLobby)
        {
            observedBuildingIds.Add(building.Id);
            return;
        }

        if (GetNowMs() < mapEventSuppressionUntilMs)
        {
            observedBuildingIds.Add(building.Id);
            return;
        }

        if (!observedBuildingIds.Add(building.Id))
        {
            return;
        }

        int bx = building.Tile_G.x;
        int by = building.Tile_G.y;
        short bz = building.Tile_G.z;
        byte rotation = (byte)building.Rotation_G.Value;
        byte layer = ToLayerByte(bz);
        string definitionId = building.Definition.Id.ToString();

        if (!TrySendBuildCommandInternal(definitionId, bx, by, bz, rotation, layer, fromMapHook: true, out string message))
        {
            logger.Warning?.Log($"[MP_HOOK] Auto build command failed id={definitionId} pos=({bx},{by},{bz}) rot={rotation} layer={layer} reason={message}");
            return;
        }

        logger.Info?.Log($"[MP_HOOK] Auto build command sent id={definitionId} pos=({bx},{by},{bz}) rot={rotation} layer={layer} msg={message}");
    }

    private void HandleMapBeforeBuildingRemoved(BuildingModel building)
    {
        if (!mapHooksReady || suppressMapEventCommands || !IsInLobby)
        {
            observedBuildingIds.Remove(building.Id);
            return;
        }

        if (GetNowMs() < mapEventSuppressionUntilMs)
        {
            observedBuildingIds.Remove(building.Id);
            return;
        }

        observedBuildingIds.Remove(building.Id);

        int bx = building.Tile_G.x;
        int by = building.Tile_G.y;
        short bz = building.Tile_G.z;
        byte layer = ToLayerByte(bz);

        if (!TrySendDeleteCommandInternal(bx, by, bz, layer, fromMapHook: true, out string message))
        {
            logger.Warning?.Log($"[MP_HOOK] Auto delete command failed pos=({bx},{by},{bz}) layer={layer} reason={message}");
            return;
        }

        logger.Info?.Log($"[MP_HOOK] Auto delete command sent pos=({bx},{by},{bz}) layer={layer} msg={message}");
    }

    private void HandleMapIslandAdded(IslandModel island)
    {
        CacheKnownIslandDefinition(island.Definition);

        if (!mapHooksReady || suppressMapEventCommands || !IsInLobby)
        {
            observedIslandIds.Add(island.Id);
            return;
        }

        if (GetNowMs() < mapEventSuppressionUntilMs)
        {
            observedIslandIds.Add(island.Id);
            return;
        }

        if (!observedIslandIds.Add(island.Id))
        {
            return;
        }

        int ix = island.Position.x;
        int iy = island.Position.y;
        short iz = island.Position.z;
        byte rotation = (byte)island.Rotation.Value;
        string definitionId = island.DefinitionId.ToString();

        if (!TrySendCreateIslandCommandInternal(definitionId, ix, iy, iz, rotation, fromMapHook: true, out string message))
        {
            logger.Warning?.Log($"[MP_HOOK] Auto create island command failed id={definitionId} pos=({ix},{iy},{iz}) rot={rotation} reason={message}");
            return;
        }

        logger.Info?.Log($"[MP_HOOK] Auto create island command sent id={definitionId} pos=({ix},{iy},{iz}) rot={rotation} msg={message}");
    }

    private void HandleMapBeforeIslandRemoved(IslandModel island)
    {
        if (!mapHooksReady || suppressMapEventCommands || !IsInLobby)
        {
            observedIslandIds.Remove(island.Id);
            return;
        }

        if (GetNowMs() < mapEventSuppressionUntilMs)
        {
            observedIslandIds.Remove(island.Id);
            return;
        }

        observedIslandIds.Remove(island.Id);

        int ix = island.Position.x;
        int iy = island.Position.y;
        short iz = island.Position.z;
        if (!TrySendDeleteIslandCommandInternal(ix, iy, iz, fromMapHook: true, out string message))
        {
            logger.Warning?.Log($"[MP_HOOK] Auto delete island command failed pos=({ix},{iy},{iz}) reason={message}");
            return;
        }

        logger.Info?.Log($"[MP_HOOK] Auto delete island command sent pos=({ix},{iy},{iz}) msg={message}");
    }

    private static byte ToLayerByte(short z)
    {
        if (z < byte.MinValue)
        {
            return byte.MinValue;
        }

        if (z > byte.MaxValue)
        {
            return byte.MaxValue;
        }

        return (byte)z;
    }

    private void ProcessPendingAutoLeaveOnNullMap()
    {
        if (!pendingAutoLeaveOnNullMap || !IsInLobby)
        {
            return;
        }

        if (trackedLocalPlayer is null || trackedLocalPlayer.CurrentMap != default)
        {
            pendingAutoLeaveOnNullMap = false;
            autoLeaveOnNullMapAtMs = 0;
            return;
        }

        if (GetNowMs() < autoLeaveOnNullMapAtMs)
        {
            return;
        }

        pendingAutoLeaveOnNullMap = false;
        autoLeaveOnNullMapAtMs = 0;
        logger.Info?.Log("[MP_HOOK] Auto leave lobby triggered by world exit");
        LeaveLobby();
    }

    private void QueueShadowSnapshotApply(string reason)
    {
        if (IsHost)
        {
            return;
        }

        pendingShadowSnapshotApply = true;
        pendingShadowSnapshotApplyAttempts = 0;
        nextShadowSnapshotApplyAtMs = GetNowMs();
        pendingShadowSnapshotApplyReason = reason;
        logger.Info?.Log($"[MP_SNAPSHOT] Queued real-map apply reason={reason} entities={worldState.Count}");
    }

    private void ProcessPendingShadowSnapshotApply()
    {
        if (!pendingShadowSnapshotApply)
        {
            return;
        }

        if (!IsInLobby || IsHost || !worldSyncEnabled)
        {
            pendingShadowSnapshotApply = false;
            pendingShadowSnapshotApplyAttempts = 0;
            nextShadowSnapshotApplyAtMs = 0;
            pendingShadowSnapshotApplyReason = string.Empty;
            return;
        }

        if (trackedMap == default)
        {
            return;
        }

        long now = GetNowMs();
        if (now < nextShadowSnapshotApplyAtMs)
        {
            return;
        }

        if (TryIsSimulationGraphUpdating(out bool isUpdating) && isUpdating)
        {
            pendingShadowSnapshotApplyAttempts++;
            nextShadowSnapshotApplyAtMs = now + ShadowSnapshotApplyBusyDelayMs;
            if (pendingShadowSnapshotApplyAttempts == 1 ||
                pendingShadowSnapshotApplyAttempts % ShadowSnapshotApplyWarnEveryAttempts == 0)
            {
                logger.Info?.Log($"[MP_SNAPSHOT] Real-map apply waiting for simulation idle reason={pendingShadowSnapshotApplyReason} attempts={pendingShadowSnapshotApplyAttempts}");
            }

            return;
        }

        if (TryApplyShadowSnapshotToRealMap(out string applyError))
        {
            logger.Info?.Log($"[MP_SNAPSHOT] Real-map apply completed reason={pendingShadowSnapshotApplyReason} attempts={pendingShadowSnapshotApplyAttempts}");
            pendingShadowSnapshotApply = false;
            pendingShadowSnapshotApplyAttempts = 0;
            nextShadowSnapshotApplyAtMs = 0;
            pendingShadowSnapshotApplyReason = string.Empty;
            return;
        }

        pendingShadowSnapshotApplyAttempts++;
        bool simulationBusy = applyError.IndexOf("SimulationGraph is currently updating", StringComparison.OrdinalIgnoreCase) >= 0;
        int delayMs = simulationBusy ? ShadowSnapshotApplyRetryDelayMs : 500;
        nextShadowSnapshotApplyAtMs = now + delayMs;

        if (pendingShadowSnapshotApplyAttempts == 1 ||
            pendingShadowSnapshotApplyAttempts % ShadowSnapshotApplyWarnEveryAttempts == 0 ||
            !simulationBusy)
        {
            logger.Warning?.Log($"[MP_SNAPSHOT] Real-map apply retry scheduled reason={pendingShadowSnapshotApplyReason} attempts={pendingShadowSnapshotApplyAttempts} delayMs={delayMs} error={applyError}");
        }
    }

    private bool TryIsSimulationGraphUpdating(out bool isUpdating)
    {
        isUpdating = false;
        if (trackedMap == default)
        {
            return false;
        }

        try
        {
            object? simulator = trackedMap.Simulator;
            if (simulator is null)
            {
                return false;
            }

            Type simulatorType = simulator.GetType();
            object? graph = simulatorType
                .GetField("SimulationGraph", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetValue(simulator);
            if (graph is null)
            {
                return false;
            }

            Type graphType = graph.GetType();
            PropertyInfo? isUpdatingProperty = graphType.GetProperty(
                "IsUpdating",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (isUpdatingProperty?.GetValue(graph) is bool value)
            {
                isUpdating = value;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool TryRunWithMapBunchEdit(Action action, out string error)
    {
        if (trackedMap == default)
        {
            error = "real map is not loaded";
            return false;
        }

        object editor = trackedMap;
        Type editorType = editor.GetType();

        MethodInfo? start = editorType.GetMethod(
            "StartBunchEdit",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo? finish = editorType.GetMethod(
            "FinishBunchEdit",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (start is null || finish is null)
        {
            action();
            error = string.Empty;
            return true;
        }

        object? scope = null;
        try
        {
            scope = start.Invoke(editor, Array.Empty<object>());
            action();
            finish.Invoke(editor, new[] { scope });
            error = string.Empty;
            return true;
        }
        catch (TargetInvocationException ex)
        {
            try
            {
                if (scope is not null)
                {
                    finish.Invoke(editor, new[] { scope });
                }
            }
            catch
            {
                // ignored
            }

            error = ex.InnerException?.Message ?? ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            try
            {
                if (scope is not null)
                {
                    finish.Invoke(editor, new[] { scope });
                }
            }
            catch
            {
                // ignored
            }

            error = ex.Message;
            return false;
        }
    }

    private bool TryRebuildShadowWorldFromLiveMap(out string error)
    {
        if (!IsHost)
        {
            error = "only host can rebuild shadow world from live map";
            return false;
        }

        if (trackedMap == default)
        {
            error = "live map is not available";
            return false;
        }

        worldState.Clear();
        foreach (IslandModel island in trackedMap.Islands)
        {
            CacheKnownIslandDefinition(island.Definition);
            int ix = island.Position.x;
            int iy = island.Position.y;
            short iz = island.Position.z;
            CreateIslandCommand command = new()
            {
                LocalCommandId = 0,
                IssuerPlayerId = 0,
                IslandDefinitionId = island.DefinitionId.ToString(),
                X = ix,
                Y = iy,
                Z = iz,
                Rotation = (byte)island.Rotation.Value
            };
            worldState.ApplyCreateIsland(command);
        }

        foreach (BuildingModel building in trackedMap.Buildings)
        {
            CacheKnownBuildingDefinition(building.Definition);
            int bx = building.Tile_G.x;
            int by = building.Tile_G.y;
            short bz = building.Tile_G.z;
            BuildCommand command = new()
            {
                LocalCommandId = 0,
                IssuerPlayerId = 0,
                BuildingDefinitionId = building.Definition.Id.ToString(),
                X = bx,
                Y = by,
                Z = bz,
                Rotation = (byte)building.Rotation_G.Value,
                Layer = ToLayerByte(bz),
                ExtraPayload = Array.Empty<byte>()
            };
            worldState.ApplyBuild(command);
        }

        localWorldRevision++;
        snapshotSentPeers.Clear();
        error = string.Empty;
        return true;
    }

    private void CacheKnownBuildingDefinition(IBuildingDefinition definition)
    {
        if (definition is null)
        {
            return;
        }

        string definitionId = definition.Id.ToString();
        if (string.IsNullOrWhiteSpace(definitionId))
        {
            return;
        }

        knownBuildingDefinitions[definitionId] = definition;
    }

    private void CacheKnownIslandDefinition(IIslandDefinition definition)
    {
        if (definition is null)
        {
            return;
        }

        string definitionId = definition.Id.ToString();
        if (string.IsNullOrWhiteSpace(definitionId))
        {
            return;
        }

        knownIslandDefinitions[definitionId] = definition;
    }

    private bool TryResolveBuildingDefinition(string definitionId, out IBuildingDefinition definition)
    {
        definition = default!;
        if (string.IsNullOrWhiteSpace(definitionId))
        {
            return false;
        }

        if (knownBuildingDefinitions.TryGetValue(definitionId, out IBuildingDefinition known))
        {
            definition = known;
            return true;
        }

        if (TryResolveDefinitionViaGameMode(definitionId, out IBuildingDefinition resolved))
        {
            definition = resolved;
            knownBuildingDefinitions[definitionId] = resolved;
            return true;
        }

        return false;
    }

    private bool TryResolveDefinitionViaGameMode(string definitionId, out IBuildingDefinition definition)
    {
        definition = default!;
        if (!StaticGameCoreAccessor.HasInstance())
        {
            return false;
        }

        object? mode = StaticGameCoreAccessor.G?.Mode;
        if (mode is null)
        {
            return false;
        }

        FieldInfo? buildingsField = mode.GetType().GetField("Buildings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object? buildings = buildingsField?.GetValue(mode);
        if (buildings is null)
        {
            return false;
        }

        MethodInfo? tryGetDefinition = buildings.GetType().GetMethod(
            "TryGetDefinition",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(BuildingDefinitionId), typeof(IBuildingDefinition).MakeByRefType() },
            modifiers: null);

        if (tryGetDefinition is null)
        {
            return false;
        }

        object?[] args = new object?[] { new BuildingDefinitionId(definitionId), null };
        object? result = tryGetDefinition.Invoke(buildings, args);
        if (result is bool ok && ok && args[1] is IBuildingDefinition resolved)
        {
            definition = resolved;
            return true;
        }

        return false;
    }

    private bool TryResolveIslandDefinition(string definitionId, out IIslandDefinition definition)
    {
        definition = default!;
        if (string.IsNullOrWhiteSpace(definitionId))
        {
            return false;
        }

        if (knownIslandDefinitions.TryGetValue(definitionId, out IIslandDefinition known))
        {
            definition = known;
            return true;
        }

        if (TryResolveIslandDefinitionViaGameMode(definitionId, out IIslandDefinition resolved))
        {
            definition = resolved;
            knownIslandDefinitions[definitionId] = resolved;
            return true;
        }

        return false;
    }

    private bool TryResolveIslandDefinitionViaGameMode(string definitionId, out IIslandDefinition definition)
    {
        definition = default!;
        if (!StaticGameCoreAccessor.HasInstance())
        {
            return false;
        }

        object? mode = StaticGameCoreAccessor.G?.Mode;
        if (mode is null)
        {
            return false;
        }

        FieldInfo? islandsField = mode.GetType().GetField("Islands", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object? islands = islandsField?.GetValue(mode);
        if (islands is null)
        {
            return false;
        }

        MethodInfo? tryGetDefinition = islands.GetType().GetMethod(
            "TryGetDefinition",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(IslandDefinitionId), typeof(IIslandDefinition).MakeByRefType() },
            modifiers: null);

        if (tryGetDefinition is null)
        {
            return false;
        }

        object?[] args = new object?[] { new IslandDefinitionId(definitionId), null };
        object? result = tryGetDefinition.Invoke(islands, args);
        if (result is bool ok && ok && args[1] is IIslandDefinition resolved)
        {
            definition = resolved;
            return true;
        }

        return false;
    }

    private bool TryApplyCommandToRealMap(ICommand command, out string error)
    {
        if (trackedMap == default)
        {
            error = "real map is not loaded";
            return false;
        }

        bool success = false;
        string localError = string.Empty;
        try
        {
            RunWithSuppressedMapEvents(() =>
            {
                success = command switch
                {
                    BuildCommand build => TryApplyBuildToRealMapCore(build, out localError),
                    DeleteCommand delete => TryApplyDeleteToRealMapCore(delete, out localError),
                    CreateIslandCommand createIsland => TryApplyCreateIslandToRealMapCore(createIsland, out localError),
                    DeleteIslandCommand deleteIsland => TryApplyDeleteIslandToRealMapCore(deleteIsland, out localError),
                    _ => false
                };

                if (!success &&
                    command is not BuildCommand &&
                    command is not DeleteCommand &&
                    command is not CreateIslandCommand &&
                    command is not DeleteIslandCommand)
                {
                    localError = $"unsupported command type for real map apply: {command.Type}";
                }
            });
        }
        catch (Exception ex)
        {
            success = false;
            localError = ex.Message;
        }

        error = localError;
        if (success)
        {
            mapEventSuppressionUntilMs = GetNowMs() + 500;
        }

        return success;
    }

    private bool TryApplyBuildToRealMapCore(BuildCommand command, out string error)
    {
        if (trackedMap == default)
        {
            error = "real map is not loaded";
            return false;
        }

        if (!TryResolveBuildingDefinition(command.BuildingDefinitionId, out IBuildingDefinition definition))
        {
            error = $"definition not found: {command.BuildingDefinitionId}";
            return false;
        }

        if (!TryMakeGlobalTransform(command.X, command.Y, command.Z, command.Rotation, out Game.Core.Coordinates.GlobalTileTransform transform, out error))
        {
            return false;
        }

        IBuildingConfiguration configuration = default!;
        _ = definition.TryCreateConfiguration(out configuration);

        trackedMap.CreateBuilding(definition, ref transform, configuration);
        return true;
    }

    private bool TryApplyDeleteToRealMapCore(DeleteCommand command, out string error)
    {
        if (trackedMap == default)
        {
            error = "real map is not loaded";
            return false;
        }

        if (!TryMakeGlobalCoordinate(command.X, command.Y, command.Z, out Game.Core.Coordinates.GlobalTileCoordinate position, out error))
        {
            return false;
        }

        if (!trackedMap.TryGetBuilding(position, out BuildingModel building))
        {
            error = string.Empty;
            return true;
        }

        BuildingId id = building.Id;
        trackedMap.DeleteBuilding(ref id);
        error = string.Empty;
        return true;
    }

    private bool TryApplyCreateIslandToRealMapCore(CreateIslandCommand command, out string error)
    {
        if (trackedMap == default)
        {
            error = "real map is not loaded";
            return false;
        }

        if (!TryResolveIslandDefinition(command.IslandDefinitionId, out IIslandDefinition definition))
        {
            error = $"island definition not found: {command.IslandDefinitionId}";
            return false;
        }

        if (!TryMakeGlobalChunkTransform(command.X, command.Y, command.Z, command.Rotation, out Game.Core.Coordinates.GlobalChunkTransform transform, out error))
        {
            return false;
        }

        IIslandConfiguration configuration = default!;
        _ = definition.TryCreateConfiguration(out configuration);

        trackedMap.CreateIsland(definition, transform, configuration);
        return true;
    }

    private bool TryApplyDeleteIslandToRealMapCore(DeleteIslandCommand command, out string error)
    {
        if (trackedMap == default)
        {
            error = "real map is not loaded";
            return false;
        }

        if (!TryMakeGlobalChunkCoordinate(command.X, command.Y, command.Z, out Game.Core.Coordinates.GlobalChunkCoordinate position, out error))
        {
            return false;
        }

        if (!trackedMap.TryGetIsland(position, out IslandModel island))
        {
            error = string.Empty;
            return true;
        }

        IslandId id = island.Id;
        trackedMap.DeleteIsland(ref id);
        error = string.Empty;
        return true;
    }

    private bool TryApplyShadowSnapshotToRealMap(out string error)
    {
        if (trackedMap == default)
        {
            error = "real map is not loaded";
            return false;
        }

        List<WorldIslandState> islands = new(worldState.GetAllIslands());
        islands.Sort(static (a, b) =>
        {
            int cmp = a.Z.CompareTo(b.Z);
            if (cmp != 0) return cmp;
            cmp = a.Y.CompareTo(b.Y);
            if (cmp != 0) return cmp;
            cmp = a.X.CompareTo(b.X);
            if (cmp != 0) return cmp;
            return StringComparer.Ordinal.Compare(a.IslandDefinitionId, b.IslandDefinitionId);
        });

        List<WorldEntityState> entities = new(worldState.GetAllEntities());
        entities.Sort(static (a, b) =>
        {
            int cmp = a.Layer.CompareTo(b.Layer);
            if (cmp != 0) return cmp;
            cmp = a.Z.CompareTo(b.Z);
            if (cmp != 0) return cmp;
            cmp = a.Y.CompareTo(b.Y);
            if (cmp != 0) return cmp;
            cmp = a.X.CompareTo(b.X);
            if (cmp != 0) return cmp;
            return StringComparer.Ordinal.Compare(a.BuildingDefinitionId, b.BuildingDefinitionId);
        });

        bool success = true;
        string localError = string.Empty;
        try
        {
            if (!TryRunWithMapBunchEdit(() =>
            {
                RunWithSuppressedMapEvents(() =>
                {
                    if (!TryClearRealMapCore(out localError))
                    {
                        success = false;
                        return;
                    }

                    foreach (WorldIslandState island in islands)
                    {
                        CreateIslandCommand command = new()
                        {
                            LocalCommandId = 0,
                            IssuerPlayerId = 0,
                            IslandDefinitionId = island.IslandDefinitionId,
                            X = island.X,
                            Y = island.Y,
                            Z = island.Z,
                            Rotation = island.Rotation
                        };

                        if (!TryApplyCreateIslandToRealMapCore(command, out localError))
                        {
                            success = false;
                            return;
                        }
                    }

                    foreach (WorldEntityState entity in entities)
                    {
                        BuildCommand command = new()
                        {
                            LocalCommandId = 0,
                            IssuerPlayerId = 0,
                            BuildingDefinitionId = entity.BuildingDefinitionId,
                            X = entity.X,
                            Y = entity.Y,
                            Z = entity.Z,
                            Rotation = entity.Rotation,
                            Layer = entity.Layer,
                            ExtraPayload = Array.Empty<byte>()
                        };

                        if (!TryApplyBuildToRealMapCore(command, out localError))
                        {
                            success = false;
                            return;
                        }
                    }
                });
            }, out string bunchEditError))
            {
                success = false;
                localError = bunchEditError;
            }
        }
        catch (Exception ex)
        {
            success = false;
            localError = ex.Message;
        }

        error = localError;
        if (success)
        {
            mapEventSuppressionUntilMs = GetNowMs() + 2000;
            logger.Info?.Log($"[MP_HOOK] Applied shadow snapshot to real map islands={islands.Count} buildings={entities.Count}");
        }

        return success;
    }

    private bool TryClearRealMapCore(out string error)
    {
        if (trackedMap == default)
        {
            error = "real map is not loaded";
            return false;
        }

        List<BuildingId> toDelete = new();
        foreach (BuildingModel building in trackedMap.Buildings)
        {
            toDelete.Add(building.Id);
        }

        foreach (BuildingId target in toDelete)
        {
            BuildingId id = target;
            trackedMap.DeleteBuilding(ref id);
        }

        List<IslandId> islandsToDelete = new();
        foreach (IslandModel island in trackedMap.Islands)
        {
            islandsToDelete.Add(island.Id);
        }

        foreach (IslandId target in islandsToDelete)
        {
            IslandId id = target;
            trackedMap.DeleteIsland(ref id);
        }

        error = string.Empty;
        return true;
    }

    private void RunWithSuppressedMapEvents(Action action)
    {
        bool previousSuppress = suppressMapEventCommands;
        suppressMapEventCommands = true;
        mapEventSuppressionUntilMs = GetNowMs() + 2000;

        try
        {
            action();
        }
        finally
        {
            suppressMapEventCommands = previousSuppress;
            mapEventSuppressionUntilMs = GetNowMs() + 500;
        }
    }

    private static bool TryMakeGlobalCoordinate(int x, int y, int z, out Game.Core.Coordinates.GlobalTileCoordinate coordinate, out string error)
    {
        if (z < short.MinValue || z > short.MaxValue)
        {
            coordinate = default;
            error = $"z out of range for short: {z}";
            return false;
        }

        coordinate = new Game.Core.Coordinates.GlobalTileCoordinate(x, y, (short)z);
        error = string.Empty;
        return true;
    }

    private static bool TryMakeGlobalChunkCoordinate(int x, int y, int z, out Game.Core.Coordinates.GlobalChunkCoordinate coordinate, out string error)
    {
        if (z < short.MinValue || z > short.MaxValue)
        {
            coordinate = default;
            error = $"z out of range for short: {z}";
            return false;
        }

        coordinate = new Game.Core.Coordinates.GlobalChunkCoordinate(x, y, (short)z);
        error = string.Empty;
        return true;
    }

    private static bool TryMakeGlobalTransform(int x, int y, int z, byte rotation, out Game.Core.Coordinates.GlobalTileTransform transform, out string error)
    {
        if (!TryMakeGlobalCoordinate(x, y, z, out Game.Core.Coordinates.GlobalTileCoordinate coordinate, out error))
        {
            transform = default;
            return false;
        }

        transform = new Game.Core.Coordinates.GlobalTileTransform(
            coordinate,
            new Game.Core.Coordinates.GridRotation(rotation));
        return true;
    }

    private static bool TryMakeGlobalChunkTransform(int x, int y, int z, byte rotation, out Game.Core.Coordinates.GlobalChunkTransform transform, out string error)
    {
        if (!TryMakeGlobalChunkCoordinate(x, y, z, out Game.Core.Coordinates.GlobalChunkCoordinate coordinate, out error))
        {
            transform = default;
            return false;
        }

        transform = new Game.Core.Coordinates.GlobalChunkTransform(
            coordinate,
            new Game.Core.Coordinates.GridRotation(rotation));
        return true;
    }

    private bool TryAcceptAndBroadcastCommand(uint issuerPlayerId, ulong issuerSteamId, ICommand command, out string error)
    {
        CommandValidationResult validation = commandValidator.Validate(issuerPlayerId, command);
        if (!validation.Success)
        {
            error = validation.Reason.ToString();
            if (issuerSteamId != 0 && issuerSteamId != GetLocalSteamId())
            {
                SendReject(issuerSteamId, command.LocalCommandId, validation.Reason, error);
            }

            return false;
        }

        if (!worldState.TryApplyCommand(command, out error))
        {
            if (issuerSteamId != 0 && issuerSteamId != GetLocalSteamId())
            {
                SendReject(issuerSteamId, command.LocalCommandId, CommandRejectReason.InvalidPayload, error);
            }

            return false;
        }

        nextGlobalSequence++;
        localWorldRevision++;
        lastCommandSummary = $"HostAccepted {FormatCommand(command)} rev={localWorldRevision}";

        AuthoritativeCommandMessage authoritative = new(nextGlobalSequence, localWorldRevision, command);
        byte[] payload = authoritative.Serialize();

        bool sentAny = false;
        foreach (ulong peerSteamId in connectedPeers)
        {
            if (SendPacket(peerSteamId, NetChannel.Commands, MessageType.AuthoritativeCommand, payload, P2PSend.Reliable))
            {
                sentAny = true;
            }
        }

        if (issuerSteamId != 0 && issuerSteamId != GetLocalSteamId())
        {
            SendAck(issuerSteamId, command.LocalCommandId, nextGlobalSequence, localWorldRevision);
        }

        logger.Info?.Log($"[MP_COMMAND] Accepted {command.Type} local={command.LocalCommandId} issuer={issuerPlayerId} global={nextGlobalSequence} revision={localWorldRevision} broadcast={sentAny}");
        return true;
    }

    private void ApplyAuthoritativeCommand(ulong senderSteamId, AuthoritativeCommandMessage message)
    {
        if (!worldState.TryApplyCommand(message.Command, out string error))
        {
            logger.Warning?.Log($"[MP_COMMAND] Failed to apply authoritative command from={senderSteamId} reason={error}");
            return;
        }

        localWorldRevision = message.WorldRevision;
        lastCommandSummary = $"AuthoritativeApply {FormatCommand(message.Command)} rev={message.WorldRevision}";
        if (message.GlobalSequence > nextGlobalSequence)
        {
            nextGlobalSequence = message.GlobalSequence;
        }

        if (message.Command.IssuerPlayerId == GetLocalPlayerId() &&
            pendingLocalCommands.Remove(message.Command.LocalCommandId, out PendingLocalCommand pending))
        {
            long age = GetNowMs() - pending.CreatedAtMs;
            logger.Info?.Log($"[MP_COMMAND] Local command confirmed by authoritative local={message.Command.LocalCommandId} ageMs={age}");
        }

        if (!IsHost && worldSyncEnabled)
        {
            if (!TryApplyCommandToRealMap(message.Command, out string applyError))
            {
                logger.Warning?.Log($"[MP_COMMAND] Authoritative applied to shadow, but real map apply failed: {applyError}");
                if (applyError.IndexOf("SimulationGraph is currently updating", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    QueueShadowSnapshotApply("authoritative-command-retry");
                }
            }
        }

        logger.Info?.Log($"[MP_COMMAND] Applied authoritative {message.Command.Type} global={message.GlobalSequence} revision={message.WorldRevision} from={senderSteamId}");
    }

    private void HandleCommandAck(ulong senderSteamId, CommandAckMessage ack)
    {
        if (pendingLocalCommands.Remove(ack.LocalCommandId, out PendingLocalCommand pending))
        {
            long age = GetNowMs() - pending.CreatedAtMs;
            logger.Info?.Log($"[MP_COMMAND] Ack local={ack.LocalCommandId} global={ack.GlobalSequence} revision={ack.WorldRevision} ageMs={age} from={senderSteamId}");
        }
        else
        {
            logger.Info?.Log($"[MP_COMMAND] Ack local={ack.LocalCommandId} global={ack.GlobalSequence} revision={ack.WorldRevision} from={senderSteamId}");
        }
    }

    private void HandleCommandReject(ulong senderSteamId, CommandRejectMessage reject)
    {
        pendingLocalCommands.Remove(reject.LocalCommandId);
        logger.Warning?.Log($"[MP_COMMAND] Reject local={reject.LocalCommandId} reason={reject.Reason} text={reject.ReasonText} from={senderSteamId}");
    }

    private void SendAck(ulong recipientSteamId, uint localCommandId, ulong globalSeq, ulong worldRevision)
    {
        CommandAckMessage ack = new(localCommandId, globalSeq, worldRevision);
        _ = SendPacket(recipientSteamId, NetChannel.Control, MessageType.CommandAck, ack.Serialize(), P2PSend.Reliable);
    }

    private void SendReject(ulong recipientSteamId, uint localCommandId, CommandRejectReason reason, string reasonText)
    {
        CommandRejectMessage reject = new(localCommandId, reason, reasonText);
        _ = SendPacket(recipientSteamId, NetChannel.Control, MessageType.CommandReject, reject.Serialize(), P2PSend.Reliable);
    }

    private void SendSnapshotToPeer(ulong recipientSteamId, bool force = false)
    {
        if (!IsHost || recipientSteamId == 0 || recipientSteamId == GetLocalSteamId())
        {
            return;
        }

        if (!force && snapshotSentPeers.Contains(recipientSteamId))
        {
            return;
        }

        byte[] snapshotBytes = worldState.SerializeSnapshot();
        ulong layoutHash = worldState.ComputeLayoutHash();
        int totalBytes = snapshotBytes.Length;
        int totalChunks = totalBytes == 0 ? 0 : (int)Math.Ceiling(totalBytes / (double)SnapshotChunkBytes);
        uint snapshotId = nextSnapshotId++;

        SnapshotBeginMessage begin = new(snapshotId, localWorldRevision, totalChunks, totalBytes, layoutHash);
        if (!SendPacket(recipientSteamId, NetChannel.Snapshot, MessageType.SnapshotBegin, begin.Serialize(), P2PSend.Reliable))
        {
            return;
        }

        for (int i = 0; i < totalChunks; i++)
        {
            int offset = i * SnapshotChunkBytes;
            int chunkLength = Math.Min(SnapshotChunkBytes, totalBytes - offset);
            byte[] chunk = new byte[chunkLength];
            Buffer.BlockCopy(snapshotBytes, offset, chunk, 0, chunkLength);

            SnapshotChunkMessage chunkMessage = new(snapshotId, i, chunk);
            _ = SendPacket(recipientSteamId, NetChannel.Snapshot, MessageType.SnapshotChunk, chunkMessage.Serialize(), P2PSend.Reliable);
        }

        SnapshotEndMessage end = new(snapshotId);
        _ = SendPacket(recipientSteamId, NetChannel.Snapshot, MessageType.SnapshotEnd, end.Serialize(), P2PSend.Reliable);

        SendWorldHash(recipientSteamId);
        snapshotSentPeers.Add(recipientSteamId);

        logger.Info?.Log($"[MP_SNAPSHOT] Sent snapshot id={snapshotId} to={recipientSteamId} bytes={totalBytes} chunks={totalChunks} revision={localWorldRevision} hash={layoutHash}");
    }

    private void HandleSnapshotBegin(ulong senderSteamId, SnapshotBeginMessage begin)
    {
        if (begin.TotalChunks < 0 || begin.TotalBytes < 0)
        {
            logger.Warning?.Log($"[MP_SNAPSHOT] Invalid snapshot begin from={senderSteamId} chunks={begin.TotalChunks} bytes={begin.TotalBytes}");
            return;
        }

        snapshotBuffers[senderSteamId] = new SnapshotReceiveBuffer(begin);
        SnapshotStatusText = $"Receiving snapshot {begin.SnapshotId}: 0/{begin.TotalChunks}";
        logger.Info?.Log($"[MP_SNAPSHOT] Begin id={begin.SnapshotId} from={senderSteamId} chunks={begin.TotalChunks} bytes={begin.TotalBytes} revision={begin.WorldRevision}");
    }

    private void HandleSnapshotChunk(ulong senderSteamId, SnapshotChunkMessage chunk)
    {
        if (!snapshotBuffers.TryGetValue(senderSteamId, out SnapshotReceiveBuffer? buffer))
        {
            logger.Warning?.Log($"[MP_SNAPSHOT] Chunk ignored without begin from={senderSteamId} id={chunk.SnapshotId}");
            return;
        }

        if (!buffer.TryAddChunk(chunk))
        {
            logger.Warning?.Log($"[MP_SNAPSHOT] Chunk rejected from={senderSteamId} id={chunk.SnapshotId} index={chunk.ChunkIndex}");
            return;
        }

        SnapshotStatusText = $"Receiving snapshot {chunk.SnapshotId}: {buffer.ReceivedChunkCount}/{buffer.Begin.TotalChunks}";
    }

    private void HandleSnapshotEnd(ulong senderSteamId, SnapshotEndMessage end)
    {
        if (!snapshotBuffers.TryGetValue(senderSteamId, out SnapshotReceiveBuffer? buffer))
        {
            logger.Warning?.Log($"[MP_SNAPSHOT] End ignored without begin from={senderSteamId} id={end.SnapshotId}");
            return;
        }

        if (buffer.Begin.SnapshotId != end.SnapshotId)
        {
            logger.Warning?.Log($"[MP_SNAPSHOT] End snapshot id mismatch from={senderSteamId} begin={buffer.Begin.SnapshotId} end={end.SnapshotId}");
            return;
        }

        try
        {
            byte[] payload = buffer.Assemble();
            worldState.LoadSnapshot(payload);
            localWorldRevision = buffer.Begin.WorldRevision;
            ulong localHash = worldState.ComputeLayoutHash();

            SnapshotStatusText = $"Snapshot applied id={end.SnapshotId} entities={worldState.Count}";
            logger.Info?.Log($"[MP_SNAPSHOT] Applied snapshot id={end.SnapshotId} from={senderSteamId} entities={worldState.Count} revision={localWorldRevision} localHash={localHash} remoteHash={buffer.Begin.LayoutHash}");

            if (!IsHost && worldSyncEnabled)
            {
                QueueShadowSnapshotApply($"snapshot-{end.SnapshotId}");
            }

            if (!IsHost)
            {
                SendWorldHash(senderSteamId);
            }
        }
        catch (Exception ex)
        {
            SnapshotStatusText = $"Snapshot apply failed: {ex.Message}";
            logger.Warning?.Log($"[MP_SNAPSHOT] Failed to apply snapshot id={end.SnapshotId} from={senderSteamId} error={ex.Message}");
        }
        finally
        {
            snapshotBuffers.Remove(senderSteamId);
        }
    }

    private void SendWorldHash(ulong recipientSteamId)
    {
        WorldHashMessage message = new(localWorldRevision, worldState.ComputeLayoutHash());
        _ = SendPacket(recipientSteamId, NetChannel.Control, MessageType.WorldHash, message.Serialize(), P2PSend.Reliable);
    }

    private void HandleWorldHash(ulong senderSteamId, WorldHashMessage worldHash)
    {
        ulong localHash = worldState.ComputeLayoutHash();
        if (localHash != worldHash.LayoutHash)
        {
            logger.Warning?.Log($"[MP_DESYNC] WorldHash mismatch from={senderSteamId} local={localHash} remote={worldHash.LayoutHash} localRev={localWorldRevision} remoteRev={worldHash.WorldRevision}");
            return;
        }

        logger.Info?.Log($"[MP_NET] WorldHash match from={senderSteamId} hash={localHash} revision={worldHash.WorldRevision}");
    }

    private void SendPeriodicPings()
    {
        long now = GetNowMs();
        if (now < nextPingAtMs || connectedPeers.Count == 0)
        {
            return;
        }

        nextPingAtMs = now + 1000;
        foreach (ulong peer in connectedPeers)
        {
            uint pingId = nextPingId++;
            pendingPings[pingId] = new PendingPing(peer, now);
            SendPing(peer, pingId);
        }
    }

    private void TrackConnectedPeer(ulong steamId)
    {
        if (steamId == 0 || steamId == GetLocalSteamId())
        {
            return;
        }

        bool added = connectedPeers.Add(steamId);
        if (added)
        {
            logger.Info?.Log($"[MP_NET] Peer connected steamId={steamId}");
        }
    }

    private static long GetNowMs()
    {
        return unchecked((long)(uint)Environment.TickCount);
    }

    private void EmitStatusLogIfDue()
    {
        long now = GetNowMs();
        if (now < nextStatusLogAtMs)
        {
            return;
        }

        nextStatusLogAtMs = now + 5000;

        string role = IsHost ? "host" : "client";
        string peers = connectedPeers.Count == 0 ? "none" : string.Join(",", connectedPeers);
        string rtts = peerRtts.Count == 0
            ? "none"
            : string.Join(",", BuildRttPairs(peerRtts));

        logger.Info?.Log($"[MP_NET] Status role={role} lobby={CurrentLobbyId} peers={peers} rtts={rtts} worldRev={localWorldRevision} entities={worldState.Count}");
    }

    private static IEnumerable<string> BuildRttPairs(IReadOnlyDictionary<ulong, int> values)
    {
        foreach (KeyValuePair<ulong, int> kv in values)
        {
            yield return $"{kv.Key}:{kv.Value}";
        }
    }

    public IReadOnlyList<WorldEntityState> GetDebugWorldEntities(int maxCount)
    {
        if (maxCount <= 0)
        {
            return Array.Empty<WorldEntityState>();
        }

        List<WorldEntityState> list = new(worldState.GetAllEntities());
        list.Sort(static (a, b) =>
        {
            int cmp = a.Layer.CompareTo(b.Layer);
            if (cmp != 0) return cmp;
            cmp = a.Z.CompareTo(b.Z);
            if (cmp != 0) return cmp;
            cmp = a.Y.CompareTo(b.Y);
            if (cmp != 0) return cmp;
            cmp = a.X.CompareTo(b.X);
            if (cmp != 0) return cmp;
            return StringComparer.Ordinal.Compare(a.BuildingDefinitionId, b.BuildingDefinitionId);
        });

        if (list.Count > maxCount)
        {
            list.RemoveRange(maxCount, list.Count - maxCount);
        }

        return list;
    }

    private static string FormatCommand(ICommand command)
    {
        return command switch
        {
            BuildCommand build => $"Build {build.BuildingDefinitionId} ({build.X},{build.Y},{build.Z}) rot={build.Rotation} layer={build.Layer}",
            DeleteCommand delete => $"Delete ({delete.X},{delete.Y},{delete.Z}) layer={delete.Layer}",
            CreateIslandCommand createIsland => $"CreateIsland {createIsland.IslandDefinitionId} ({createIsland.X},{createIsland.Y},{createIsland.Z}) rot={createIsland.Rotation}",
            DeleteIslandCommand deleteIsland => $"DeleteIsland ({deleteIsland.X},{deleteIsland.Y},{deleteIsland.Z})",
            _ => command.Type.ToString()
        };
    }

    private readonly struct PendingPing
    {
        public PendingPing(ulong peerSteamId, long sentAtMs)
        {
            PeerSteamId = peerSteamId;
            SentAtMs = sentAtMs;
        }

        public ulong PeerSteamId { get; }

        public long SentAtMs { get; }
    }

    private readonly struct PendingLocalCommand
    {
        public PendingLocalCommand(ICommand command, long createdAtMs)
        {
            Command = command;
            CreatedAtMs = createdAtMs;
        }

        public ICommand Command { get; }

        public long CreatedAtMs { get; }
    }

    private sealed class SnapshotReceiveBuffer
    {
        private readonly byte[][] chunks;

        public SnapshotReceiveBuffer(SnapshotBeginMessage begin)
        {
            Begin = begin;
            chunks = begin.TotalChunks == 0 ? Array.Empty<byte[]>() : new byte[begin.TotalChunks][];
        }

        public SnapshotBeginMessage Begin { get; }

        public int ReceivedChunkCount { get; private set; }

        public bool TryAddChunk(SnapshotChunkMessage chunk)
        {
            if (chunk.SnapshotId != Begin.SnapshotId)
            {
                return false;
            }

            if (chunk.ChunkIndex < 0 || chunk.ChunkIndex >= chunks.Length)
            {
                return false;
            }

            if (chunks[chunk.ChunkIndex] is not null)
            {
                return true;
            }

            chunks[chunk.ChunkIndex] = chunk.ChunkData;
            ReceivedChunkCount++;
            return true;
        }

        public byte[] Assemble()
        {
            if (Begin.TotalChunks == 0)
            {
                if (Begin.TotalBytes != 0)
                {
                    throw new InvalidDataException($"Snapshot totalBytes mismatch: chunks=0 bytes={Begin.TotalBytes}");
                }

                return Array.Empty<byte>();
            }

            if (ReceivedChunkCount != chunks.Length)
            {
                throw new InvalidDataException($"Snapshot chunk missing: received={ReceivedChunkCount} expected={chunks.Length}");
            }

            using MemoryStream stream = new(capacity: Begin.TotalBytes);
            for (int i = 0; i < chunks.Length; i++)
            {
                byte[] chunk = chunks[i] ?? throw new InvalidDataException($"Snapshot chunk null index={i}");
                stream.Write(chunk, 0, chunk.Length);
            }

            byte[] bytes = stream.ToArray();
            if (bytes.Length != Begin.TotalBytes)
            {
                throw new InvalidDataException($"Snapshot size mismatch: actual={bytes.Length} expected={Begin.TotalBytes}");
            }

            return bytes;
        }
    }
}
