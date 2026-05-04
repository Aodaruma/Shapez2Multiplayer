using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Steamworks;
using Steamworks.Data;

namespace Shapez2Multiplayer.Net;

public sealed class FacepunchSteamPlatformApi : ISteamPlatformApi
{
    private const int LobbyOperationTimeoutMs = 8000;
    private const int WaitSliceMs = 10;

    public bool IsInitialized => SteamClient.IsValid;

    public bool TryCreateLobby(out ulong lobbyId)
    {
        lobbyId = 0;
        try
        {
            if (!WaitTask(SteamMatchmaking.CreateLobbyAsync(maxMembers: 8), out Lobby? lobby) || !lobby.HasValue)
            {
                return false;
            }

            Lobby value = lobby.Value;
            value.SetFriendsOnly();
            value.SetJoinable(true);
            lobbyId = value.Id;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryJoinLobby(ulong lobbyId)
    {
        try
        {
            SteamId steamLobbyId = lobbyId;
            return WaitTask(SteamMatchmaking.JoinLobbyAsync(steamLobbyId), out Lobby? lobby) && lobby.HasValue;
        }
        catch
        {
            return false;
        }
    }

    public bool TryLeaveLobby(ulong lobbyId)
    {
        try
        {
            Lobby lobby = new((SteamId)lobbyId);
            lobby.Leave();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public ulong GetLobbyOwnerSteamId(ulong lobbyId)
    {
        try
        {
            Lobby lobby = new((SteamId)lobbyId);
            return lobby.Owner.Id;
        }
        catch
        {
            return 0;
        }
    }

    public ulong[] GetLobbyMemberSteamIds(ulong lobbyId)
    {
        try
        {
            Lobby lobby = new((SteamId)lobbyId);
            List<ulong> members = new();
            foreach (Friend member in lobby.Members)
            {
                members.Add(member.Id);
            }

            return members.ToArray();
        }
        catch
        {
            return Array.Empty<ulong>();
        }
    }

    private static bool WaitTask<T>(System.Threading.Tasks.Task<T> task, out T result)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (!task.IsCompleted && sw.ElapsedMilliseconds < LobbyOperationTimeoutMs)
        {
            SteamClient.RunCallbacks();
            Thread.Sleep(WaitSliceMs);
        }

        if (!task.IsCompleted || task.IsFaulted || task.IsCanceled)
        {
            result = default!;
            return false;
        }

        result = task.GetAwaiter().GetResult();
        return true;
    }
}
