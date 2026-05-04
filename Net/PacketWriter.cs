using System;
using System.IO;
using System.Text;

namespace Shapez2Multiplayer.Net;

public sealed class PacketWriter : IDisposable
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private readonly MemoryStream stream;
    private readonly BinaryWriter writer;
    private bool disposed;

    public PacketWriter(int capacity = 256)
    {
        stream = new MemoryStream(capacity);
        writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
    }

    public int Length => checked((int)stream.Length);

    public void WriteByte(byte value) => writer.Write(value);

    public void WriteUInt16(ushort value) => writer.Write(value);

    public void WriteUInt32(uint value) => writer.Write(value);

    public void WriteUInt64(ulong value) => writer.Write(value);

    public void WriteInt32(int value) => writer.Write(value);

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        writer.Write(bytes);
    }

    public void WriteBytesWithLength(byte[] bytes, int maxBytes)
    {
        if (bytes is null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        if (bytes.Length > maxBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), $"Payload too large: {bytes.Length} > {maxBytes}");
        }

        WriteInt32(bytes.Length);
        WriteBytes(bytes);
    }

    public void WriteString(string value, int maxUtf8Bytes)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        byte[] utf8 = Utf8.GetBytes(value);
        if (utf8.Length > maxUtf8Bytes)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"String too large: {utf8.Length} > {maxUtf8Bytes}");
        }

        WriteInt32(utf8.Length);
        WriteBytes(utf8);
    }

    public byte[] ToArray() => stream.ToArray();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        writer.Dispose();
        stream.Dispose();
    }
}
