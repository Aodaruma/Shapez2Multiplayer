using System;
using System.Collections.Generic;
using System.Linq;
using Shapez2Multiplayer.Commands;
using Shapez2Multiplayer.Net;
using Shapez2Multiplayer.Protocol;

namespace Shapez2Multiplayer.Sync;

public sealed class WorldStateStore
{
    private readonly Dictionary<WorldKey, WorldEntityState> entities = new();

    public int Count => entities.Count;

    public void Clear()
    {
        entities.Clear();
    }

    public IReadOnlyCollection<WorldEntityState> GetAllEntities()
    {
        return entities.Values;
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

    public byte[] SerializeSnapshot()
    {
        WorldEntityState[] ordered = entities
            .Values
            .OrderBy(static e => e.Layer)
            .ThenBy(static e => e.Z)
            .ThenBy(static e => e.Y)
            .ThenBy(static e => e.X)
            .ThenBy(static e => e.BuildingDefinitionId, StringComparer.Ordinal)
            .ToArray();

        using PacketWriter writer = new(capacity: Math.Max(128, ordered.Length * 48));
        writer.WriteInt32(ordered.Length);
        foreach (WorldEntityState entity in ordered)
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
        int count = reader.ReadInt32();
        if (count < 0)
        {
            throw new InvalidOperationException($"Invalid snapshot count: {count}");
        }

        entities.Clear();
        for (int i = 0; i < count; i++)
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

        if (reader.RemainingBytes != 0)
        {
            throw new InvalidOperationException($"Trailing bytes in snapshot: {reader.RemainingBytes}");
        }
    }

    public ulong ComputeLayoutHash()
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offset;
        foreach (WorldEntityState entity in entities
                     .Values
                     .OrderBy(static e => e.Layer)
                     .ThenBy(static e => e.Z)
                     .ThenBy(static e => e.Y)
                     .ThenBy(static e => e.X)
                     .ThenBy(static e => e.BuildingDefinitionId, StringComparer.Ordinal))
        {
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
}
