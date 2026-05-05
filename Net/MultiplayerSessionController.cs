using System;
using System.Collections.Generic;
using System.IO;
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

        return TrySendCommand(command, out message);
    }

    public bool TrySendDeleteCommand(int x, int y, int z, byte layer, out string message)
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

        return TrySendCommand(command, out message);
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

        PumpIncomingPackets();
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
    }

    private bool TrySendCommand(ICommand command, out string message)
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
                        SendSnapshotToPeer(senderSteamId);
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

    private void SendSnapshotToPeer(ulong recipientSteamId)
    {
        if (!IsHost || recipientSteamId == 0 || recipientSteamId == GetLocalSteamId())
        {
            return;
        }

        if (snapshotSentPeers.Contains(recipientSteamId))
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
