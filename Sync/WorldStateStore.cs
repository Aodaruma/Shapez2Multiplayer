using System;
using System.Collections.Generic;
using System.Linq;
using Shapez2Multiplayer.Commands;
using Shapez2Multiplayer.Net;

namespace Shapez2Multiplayer.Sync;

public sealed class WorldStateStore
{
    private const int SnapshotMagicV2 = 0x4D503032; // MP02

    private readonly Dictionary<WorldKey, WorldEntityState> entities = new();
    private readonly Dictionary<IslandKey, WorldIslandState> islands = new();

    public int Count => entities.Count + islands.Count;

    public int BuildingCount => entities.Count;

    public int IslandCount => islands.Count;

    public void Clear()
    {
        entities.Clear();
        islands.Clear();
    }

    public IReadOnlyCollection<WorldEntityState> GetAllEntities()
    {
        return entities.Values;
    }

    public IReadOnlyCollection<WorldIslandState> GetAllIslands()
    {
        return islands.Values;
    }

    public bool TryApplyCommand(ICommand command, out string error)
    {
        if (command is null)
        {
            error = "command is null";
            return false;
        }

        switch (command)
        {
            case BuildCommand build:
                ApplyBuild(build);
                error = string.Empty;
                return true;

            case DeleteCommand delete:
                ApplyDelete(delete);
                error = string.Empty;
                return true;

            case CreateIslandCommand createIsland:
                ApplyCreateIsland(createIsland);
                error = string.Empty;
                return true;

            case DeleteIslandCommand deleteIsland:
                ApplyDeleteIsland(deleteIsland);
                error = string.Empty;
                return true;

            default:
                error = $"unsupported command type: {command.Type}";
                return false;
        }
    }

    public void ApplyBuild(BuildCommand command)
    {
        WorldKey key = new(command.X, command.Y, command.Z, command.Layer);
        entities[key] = new WorldEntityState(
            command.BuildingDefinitionId,
            command.X,
            command.Y,
            command.Z,
            command.Rotation,
            command.Layer);
    }

    public void ApplyDelete(DeleteCommand command)
    {
        WorldKey key = new(command.X, command.Y, command.Z, command.Layer);
        entities.Remove(key);
    }

    public void ApplyCreateIsland(CreateIslandCommand command)
    {
        IslandKey key = new(command.X, command.Y, command.Z);
        islands[key] = new WorldIslandState(
            command.IslandDefinitionId,
            command.X,
            command.Y,
            command.Z,
            command.Rotation);
    }

    public void ApplyDeleteIsland(DeleteIslandCommand command)
    {
        IslandKey key = new(command.X, command.Y, command.Z);
        islands.Remove(key);
    }

    public byte[] SerializeSnapshot()
    {
        WorldIslandState[] orderedIslands = islands
            .Values
            .OrderBy(static e => e.Z)
            .ThenBy(static e => e.Y)
            .ThenBy(static e => e.X)
            .ThenBy(static e => e.IslandDefinitionId, StringComparer.Ordinal)
            .ToArray();

        WorldEntityState[] orderedBuildings = entities
            .Values
            .OrderBy(static e => e.Layer)
            .ThenBy(static e => e.Z)
            .ThenBy(static e => e.Y)
            .ThenBy(static e => e.X)
            .ThenBy(static e => e.BuildingDefinitionId, StringComparer.Ordinal)
            .ToArray();

        using PacketWriter writer = new(capacity: Math.Max(256, (orderedIslands.Length + orderedBuildings.Length) * 64));
        writer.WriteInt32(SnapshotMagicV2);

        writer.WriteInt32(orderedIslands.Length);
        foreach (WorldIslandState island in orderedIslands)
        {
            writer.WriteString(island.IslandDefinitionId, maxUtf8Bytes: 1024);
            writer.WriteInt32(island.X);
            writer.WriteInt32(island.Y);
            writer.WriteInt32(island.Z);
            writer.WriteByte(island.Rotation);
        }

        writer.WriteInt32(orderedBuildings.Length);
        foreach (WorldEntityState entity in orderedBuildings)
        {
            writer.WriteString(entity.BuildingDefinitionId, maxUtf8Bytes: 1024);
            writer.WriteInt32(entity.X);
            writer.WriteInt32(entity.Y);
            writer.WriteInt32(entity.Z);
            writer.WriteByte(entity.Rotation);
            writer.WriteByte(entity.Layer);
        }

        return writer.ToArray();
    }

    public void LoadSnapshot(byte[] snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        using PacketReader reader = new(snapshot);
        int first = reader.ReadInt32();
        if (first == SnapshotMagicV2)
        {
            LoadV2Snapshot(reader);
        }
        else
        {
            LoadLegacySnapshot(reader, first);
        }

        if (reader.RemainingBytes != 0)
        {
            throw new InvalidOperationException($"Trailing bytes in snapshot: {reader.RemainingBytes}");
        }
    }

    private void LoadV2Snapshot(PacketReader reader)
    {
        int islandCount = reader.ReadInt32();
        if (islandCount < 0)
        {
            throw new InvalidOperationException($"Invalid island snapshot count: {islandCount}");
        }

        islands.Clear();
        for (int i = 0; i < islandCount; i++)
        {
            string islandDefinitionId = reader.ReadString(maxUtf8Bytes: 1024);
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            int z = reader.ReadInt32();
            byte rotation = reader.ReadByte();

            IslandKey key = new(x, y, z);
            islands[key] = new WorldIslandState(islandDefinitionId, x, y, z, rotation);
        }

        int buildingCount = reader.ReadInt32();
        if (buildingCount < 0)
        {
            throw new InvalidOperationException($"Invalid building snapshot count: {buildingCount}");
        }

        entities.Clear();
        for (int i = 0; i < buildingCount; i++)
        {
            string buildingDefinitionId = reader.ReadString(maxUtf8Bytes: 1024);
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            int z = reader.ReadInt32();
            byte rotation = reader.ReadByte();
            byte layer = reader.ReadByte();

            WorldKey key = new(x, y, z, layer);
            entities[key] = new WorldEntityState(buildingDefinitionId, x, y, z, rotation, layer);
        }
    }

    private void LoadLegacySnapshot(PacketReader reader, int firstCount)
    {
        if (firstCount < 0)
        {
            throw new InvalidOperationException($"Invalid legacy snapshot count: {firstCount}");
        }

        islands.Clear();
        entities.Clear();
        for (int i = 0; i < firstCount; i++)
        {
            string buildingDefinitionId = reader.ReadString(maxUtf8Bytes: 1024);
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            int z = reader.ReadInt32();
            byte rotation = reader.ReadByte();
            byte layer = reader.ReadByte();

            WorldKey key = new(x, y, z, layer);
            entities[key] = new WorldEntityState(buildingDefinitionId, x, y, z, rotation, layer);
        }
    }

    public ulong ComputeLayoutHash()
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offset;

        foreach (WorldIslandState island in islands
                     .Values
                     .OrderBy(static e => e.Z)
                     .ThenBy(static e => e.Y)
                     .ThenBy(static e => e.X)
                     .ThenBy(static e => e.IslandDefinitionId, StringComparer.Ordinal))
        {
            AppendByte(1);
            AppendString(island.IslandDefinitionId);
            AppendInt(island.X);
            AppendInt(island.Y);
            AppendInt(island.Z);
            AppendByte(island.Rotation);
        }

        foreach (WorldEntityState entity in entities
                     .Values
                     .OrderBy(static e => e.Layer)
                     .ThenBy(static e => e.Z)
                     .ThenBy(static e => e.Y)
                     .ThenBy(static e => e.X)
                     .ThenBy(static e => e.BuildingDefinitionId, StringComparer.Ordinal))
        {
            AppendByte(2);
            AppendString(entity.BuildingDefinitionId);
            AppendInt(entity.X);
            AppendInt(entity.Y);
            AppendInt(entity.Z);
            AppendByte(entity.Rotation);
            AppendByte(entity.Layer);
        }

        return hash;

        void AppendString(string value)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            AppendInt(bytes.Length);
            foreach (byte b in bytes)
            {
                AppendByte(b);
            }
        }

        void AppendInt(int value)
        {
            unchecked
            {
                AppendByte((byte)value);
                AppendByte((byte)(value >> 8));
                AppendByte((byte)(value >> 16));
                AppendByte((byte)(value >> 24));
            }
        }

        void AppendByte(byte value)
        {
            hash ^= value;
            hash *= prime;
        }
    }

    private readonly struct WorldKey : IEquatable<WorldKey>
    {
        public WorldKey(int x, int y, int z, byte layer)
        {
            X = x;
            Y = y;
            Z = z;
            Layer = layer;
        }

        public int X { get; }

        public int Y { get; }

        public int Z { get; }

        public byte Layer { get; }

        public bool Equals(WorldKey other)
        {
            return X == other.X && Y == other.Y && Z == other.Z && Layer == other.Layer;
        }

        public override bool Equals(object? obj)
        {
            return obj is WorldKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z, Layer);
        }
    }

    private readonly struct IslandKey : IEquatable<IslandKey>
    {
        public IslandKey(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public int X { get; }

        public int Y { get; }

        public int Z { get; }

        public bool Equals(IslandKey other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object? obj)
        {
            return obj is IslandKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z);
        }
    }
}
