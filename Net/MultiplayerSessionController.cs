using System;
using System.Collections.Generic;
using Core.Logging;
using Shapez2Multiplayer.Protocol;
using Steamworks;
using Steamworks.Data;

namespace Shapez2Multiplayer.Net;

public sealed class MultiplayerSessionController : IDisposable
{
    private readonly ILogger logger;
    private readonly ISteamPlatformApi steamApi;
    private readonly SteamLobbyService lobbyService;
    private readonly HashSet<ulong> connectedPeers = new();
    private readonly Dictionary<uint, PendingPing> pendingPings = new();
    private readonly Dictionary<ulong, int> peerRtts = new();
    private bool disposed;
    private uint nextSequence;
    private uint nextPingId = 1;
    private long nextPingAtMs;
    private long nextStatusLogAtMs;

    public MultiplayerSessionController(ILogger logger, ISteamPlatformApi steamApi)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.steamApi = steamApi ?? throw new ArgumentNullException(nameof(steamApi));
        lobbyService = new SteamLobbyService(steamApi);
        StatusText = "Idle";
        SteamNetworking.OnP2PSessionRequest += HandleP2PSessionRequest;
        SteamNetworking.OnP2PConnectionFailed += HandleP2PConnectionFailed;
        SteamMatchmaking.OnLobbyMemberJoined += HandleLobbyMemberJoined;
    }

    public bool IsInLobby { get; private set; }

    public bool IsHost { get; private set; }

    public ulong CurrentLobbyId { get; private set; }

    public string StatusText { get; private set; }

    public ulong[] CurrentMembers => IsInLobby ? steamApi.GetLobbyMemberSteamIds(CurrentLobbyId) : Array.Empty<ulong>();

    public ulong CurrentOwnerSteamId => IsInLobby ? steamApi.GetLobbyOwnerSteamId(CurrentLobbyId) : 0;

    public int ConnectedPeerCount => connectedPeers.Count;

    public IReadOnlyDictionary<ulong, int> PeerRttMs => peerRtts;

    public bool TryHostLobby(out string message)
    {
        LobbyOperationResult result = lobbyService.HostLobby();
        if (!result.Success)
        {
            message = $"Host failed: {result.Error}";
            StatusText = message;
            logger.Warning?.Log($"[MP_LOBBY] {message}");
            return false;
        }

        IsInLobby = true;
        IsHost = true;
        CurrentLobbyId = result.LobbyId;
        nextPingAtMs = GetNowMs() + 1000;
        nextStatusLogAtMs = GetNowMs() + 2000;
        connectedPeers.Clear();
        peerRtts.Clear();
        pendingPings.Clear();
        SendHelloToKnownMembers();
        message = $"Hosting lobby: {CurrentLobbyId}";
        StatusText = message;
        logger.Info?.Log($"[MP_LOBBY] {message}");
        return true;
    }

    public bool TryJoinLobby(string lobbyIdText, out string message)
    {
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

        IsInLobby = true;
        IsHost = false;
        CurrentLobbyId = result.LobbyId;
        nextPingAtMs = GetNowMs() + 1000;
        nextStatusLogAtMs = GetNowMs() + 2000;
        connectedPeers.Clear();
        peerRtts.Clear();
        pendingPings.Clear();
        SendHelloToKnownMembers();
        message = $"Joined lobby: {CurrentLobbyId}";
        StatusText = message;
        logger.Info?.Log($"[MP_LOBBY] {message}");
        return true;
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
        connectedPeers.Clear();
        peerRtts.Clear();
        pendingPings.Clear();
        nextStatusLogAtMs = 0;
        StatusText = "Idle";
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

        PumpIncomingControlPackets();
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

    private static ulong GetLocalSteamId()
    {
        return SteamClient.IsValid ? SteamClient.SteamId : 0;
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
        SendControlPacket(recipientSteamId, MessageType.Hello, hello.Serialize());
        logger.Info?.Log($"[MP_NET] Sent Hello to={recipientSteamId}");
    }

    private void SendWelcome(ulong recipientSteamId)
    {
        WelcomeMessage welcome = new(GetLocalSteamId());
        SendControlPacket(recipientSteamId, MessageType.Welcome, welcome.Serialize());
        logger.Info?.Log($"[MP_NET] Sent Welcome to={recipientSteamId}");
    }

    private void SendPing(ulong recipientSteamId, uint pingId)
    {
        PingMessage ping = new(pingId);
        SendControlPacket(recipientSteamId, MessageType.Ping, ping.Serialize());
    }

    private void SendPong(ulong recipientSteamId, uint pingId)
    {
        PongMessage pong = new(pingId);
        SendControlPacket(recipientSteamId, MessageType.Pong, pong.Serialize());
    }

    private void SendControlPacket(ulong recipientSteamId, MessageType type, byte[] payload)
    {
        uint sessionId = unchecked((uint)CurrentLobbyId);
        uint senderPlayerId = SteamClient.IsValid ? SteamClient.SteamId.AccountId : 0;
        PacketHeader header = new(
            protocolVersion: ProtocolLimits.ProtocolVersion,
            messageType: type,
            sessionId: sessionId,
            senderPlayerId: senderPlayerId,
            sequence: ++nextSequence,
            ackSequence: 0,
            worldRevision: 0);
        byte[] packetBytes = PacketSerializer.Serialize(header, payload);

        bool sent = SteamNetworking.SendP2PPacket((SteamId)recipientSteamId, packetBytes, packetBytes.Length, (int)NetChannel.Control, P2PSend.Reliable);
        if (!sent)
        {
            logger.Warning?.Log($"[MP_NET] Failed to send packet type={type} to={recipientSteamId}");
        }
    }

    private void PumpIncomingControlPackets()
    {
        while (SteamNetworking.IsP2PPacketAvailable(channel: (int)NetChannel.Control))
        {
            P2Packet? packet = SteamNetworking.ReadP2PPacket(channel: (int)NetChannel.Control);
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

            switch (header.MessageType)
            {
                case MessageType.Hello:
                {
                    HelloMessage _ = HelloMessage.Deserialize(payload);
                    TrackConnectedPeer(senderSteamId);
                    logger.Info?.Log($"[MP_NET] Received Hello from={senderSteamId}");
                    if (IsHost)
                    {
                        SendWelcome(senderSteamId);
                    }

                    break;
                }
                case MessageType.Welcome:
                {
                    WelcomeMessage _ = WelcomeMessage.Deserialize(payload);
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
                    if (pendingPings.TryGetValue(pong.PingId, out PendingPing pending) && pending.PeerSteamId == senderSteamId)
                    {
                        pendingPings.Remove(pong.PingId);
                        int rtt = checked((int)(GetNowMs() - pending.SentAtMs));
                        peerRtts[senderSteamId] = rtt;
                        logger.Info?.Log($"[MP_NET] RTT peer={senderSteamId} rttMs={rtt}");
                    }

                    break;
                }
            }
        }
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
        string rtts = peerRtts.Count == 0 ? "none" : string.Join(",", peerRtts);

        logger.Info?.Log($"[MP_NET] Status role={role} lobby={CurrentLobbyId} peers={peers} rtts={rtts}");
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
}
