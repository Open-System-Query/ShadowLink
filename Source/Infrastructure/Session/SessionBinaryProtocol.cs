using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ShadowLink.Infrastructure.Session;

internal static class SessionBinaryProtocol
{
    private const Int32 MaxMetadataLength = 256 * 1024;
    private const Int32 MaxPayloadLength = 256 * 1024 * 1024;

    public static async Task WritePacketAsync(Stream stream, byte packetType, ReadOnlyMemory<byte> metadata, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (metadata.Length < 0 || metadata.Length > MaxMetadataLength)
        {
            throw new ArgumentOutOfRangeException(nameof(metadata));
        }

        if (payload.Length < 0 || payload.Length > MaxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }

        byte[] header = new byte[9];
        header[0] = packetType;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1, 4), metadata.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(5, 4), payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);

        if (!metadata.IsEmpty)
        {
            await stream.WriteAsync(metadata, cancellationToken).ConfigureAwait(false);
        }

        if (!payload.IsEmpty)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }

    }

    public static async Task<SessionBinaryPacket?> ReadPacketAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[]? header = await ReadExactlyOrNullAsync(stream, 9, cancellationToken).ConfigureAwait(false);
        if (header is null)
        {
            return null;
        }

        Int32 metadataLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1, 4));
        Int32 payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(5, 4));
        if (metadataLength < 0 || payloadLength < 0 || metadataLength > MaxMetadataLength || payloadLength > MaxPayloadLength)
        {
            throw new InvalidDataException();
        }

        byte[] metadata = metadataLength == 0 ? Array.Empty<byte>() : await ReadExactlyAsync(stream, metadataLength, cancellationToken).ConfigureAwait(false);
        byte[] payload = payloadLength == 0 ? Array.Empty<byte>() : await ReadExactlyAsync(stream, payloadLength, cancellationToken).ConfigureAwait(false);
        return new SessionBinaryPacket(header[0], metadata, payload);
    }

    private static async Task<byte[]?> ReadExactlyOrNullAsync(Stream stream, Int32 length, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[length];
        Int32 offset = 0;

        while (offset < length)
        {
            Int32 bytesRead = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                if (offset == 0)
                {
                    return null;
                }

                throw new EndOfStreamException();
            }

            offset += bytesRead;
        }

        return buffer;
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, Int32 length, CancellationToken cancellationToken)
    {
        byte[]? buffer = await ReadExactlyOrNullAsync(stream, length, cancellationToken).ConfigureAwait(false);
        if (buffer is null)
        {
            throw new EndOfStreamException();
        }

        return buffer;
    }
}
