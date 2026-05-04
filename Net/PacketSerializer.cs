using System;
using System.IO;
using Shapez2Multiplayer.Protocol;

namespace Shapez2Multiplayer.Net;

public static class PacketSerializer
{
    public static byte[] Serialize(PacketHeader header, ReadOnlySpan<byte> payload)
    {
        int totalBytes = checked(PacketHeader.SizeInBytes + payload.Length);
        if (totalBytes > ProtocolLimits.MaxPacketBytes)
        {
            throw new InvalidDataException($"Packet too large: {totalBytes} > {ProtocolLimits.MaxPacketBytes}");
        }

        using PacketWriter writer = new(totalBytes);
        header.Write(writer);
        writer.WriteBytes(payload);
        return writer.ToArray();
    }

    public static bool TryDeserialize(
        ReadOnlySpan<byte> packetBytes,
        out PacketHeader header,
        out byte[] payload,
        out NetworkError error)
    {
        header = default;
        payload = Array.Empty<byte>();
        error = NetworkError.None;

        if (packetBytes.Length < PacketHeader.SizeInBytes)
        {
            error = NetworkError.DeserializationFailed;
            return false;
        }

        if (packetBytes.Length > ProtocolLimits.MaxPacketBytes)
        {
            error = NetworkError.PacketTooLarge;
            return false;
        }

        try
        {
            using PacketReader reader = new(packetBytes.ToArray());
            header = PacketHeader.Read(reader);

            if (header.Magic != PacketHeader.MagicValue)
            {
                error = NetworkError.InvalidMagic;
                return false;
            }

            if (header.ProtocolVersion != ProtocolLimits.ProtocolVersion)
            {
                error = NetworkError.InvalidProtocol;
                return false;
            }

            payload = reader.ReadRemainingBytes();
            return true;
        }
        catch (Exception)
        {
            error = NetworkError.DeserializationFailed;
            header = default;
            payload = Array.Empty<byte>();
            return false;
        }
    }
}
