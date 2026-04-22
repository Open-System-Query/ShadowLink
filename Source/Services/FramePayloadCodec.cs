using System;
using System.IO;
using System.IO.Compression;
using ShadowLink.Core.Models;

namespace ShadowLink.Services;

internal static class FramePayloadCodec
{
    public static (Boolean IsCompressed, Byte[] Payload) Encode(Byte[] frameBytes, StreamColorMode colorMode, Int32 changedTileCount)
    {
        if (frameBytes.Length < 96 * 1024 || changedTileCount <= 2)
        {
            return (false, frameBytes);
        }

        if ((colorMode == StreamColorMode.Indexed4 && frameBytes.Length < 512 * 1024) ||
            (colorMode == StreamColorMode.Rgb332 && frameBytes.Length < 384 * 1024))
        {
            return (false, frameBytes);
        }

        using MemoryStream outputStream = new MemoryStream(frameBytes.Length);
        using (BrotliStream compressionStream = new BrotliStream(outputStream, CompressionLevel.Fastest, true))
        {
            compressionStream.Write(frameBytes, 0, frameBytes.Length);
        }

        Byte[] compressedBytes = outputStream.ToArray();
        Int32 minimumSavingsBytes = Math.Max(256, frameBytes.Length / 20);
        if (compressedBytes.Length >= frameBytes.Length - minimumSavingsBytes)
        {
            return (false, frameBytes);
        }

        return (true, compressedBytes);
    }

    public static Byte[] Decode(Byte[] payload, Boolean isCompressed)
    {
        if (!isCompressed)
        {
            return payload;
        }

        using MemoryStream inputStream = new MemoryStream(payload, false);
        using BrotliStream compressionStream = new BrotliStream(inputStream, CompressionMode.Decompress);
        using MemoryStream outputStream = new MemoryStream(payload.Length * 2);
        compressionStream.CopyTo(outputStream);
        return outputStream.ToArray();
    }
}
