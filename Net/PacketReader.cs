using System;
using System.IO;
using System.Text;

namespace Shapez2Multiplayer.Net;

public sealed class PacketReader : IDisposable
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private readonly MemoryStream stream;
    private readonly BinaryReader reader;
    private bool disposed;

    public PacketReader(byte[] data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        stream = new MemoryStream(data, writable: false);
        reader = new BinaryReader(stream, Utf8, leaveOpen: true);
    }

    public int RemainingBytes => checked((int)(stream.Length - stream.Position));

    public byte ReadByte() => reader.ReadByte();

    public ushort ReadUInt16() => reader.ReadUInt16();

    public uint ReadUInt32() => reader.ReadUInt32();

    public ulong ReadUInt64() => reader.ReadUInt64();

    public int ReadInt32() => reader.ReadInt32();

    public byte[] ReadBytesExact(int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (length > RemainingBytes)
        {
            throw new EndOfStreamException($"Requested={length}, Remaining={RemainingBytes}");
        }

        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new EndOfStreamException($"Requested={length}, Read={bytes.Length}");
        }

        return bytes;
    }

    public byte[] ReadBytesWithLength(int maxBytes)
    {
        int length = ReadInt32();
        if (length < 0 || length > maxBytes)
        {
            throw new InvalidDataException($"Invalid payload length: {length}, max={maxBytes}");
        }

        return ReadBytesExact(length);
    }

    public string ReadString(int maxUtf8Bytes)
    {
        int byteLength = ReadInt32();
        if (byteLength < 0 || byteLength > maxUtf8Bytes)
        {
            throw new InvalidDataException($"Invalid string length: {byteLength}, max={maxUtf8Bytes}");
        }

        byte[] bytes = ReadBytesExact(byteLength);
        return Utf8.GetString(bytes);
    }

    public byte[] ReadRemainingBytes() => ReadBytesExact(RemainingBytes);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        reader.Dispose();
        stream.Dispose();
    }
}
