using System;
using System.IO;

namespace ShadowLink.Services;

internal static class BmpFrameEncoder
{
    public static Byte[] EncodeBgra32(Int32 width, Int32 height, ReadOnlySpan<Byte> sourceBytes, Int32 sourceStride)
    {
        Int32 pixelStride = 4;
        Int32 outputStride = width * pixelStride;
        Int32 imageSize = outputStride * height;
        Int32 fileSize = 14 + 40 + imageSize;

        using MemoryStream memoryStream = new MemoryStream(fileSize);
        using BinaryWriter writer = new BinaryWriter(memoryStream);

        writer.Write((Byte)'B');
        writer.Write((Byte)'M');
        writer.Write(fileSize);
        writer.Write((Int16)0);
        writer.Write((Int16)0);
        writer.Write(14 + 40);

        writer.Write(40);
        writer.Write(width);
        writer.Write(-height);
        writer.Write((Int16)1);
        writer.Write((Int16)32);
        writer.Write(0);
        writer.Write(imageSize);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);

        for (Int32 rowIndex = 0; rowIndex < height; rowIndex++)
        {
            ReadOnlySpan<Byte> row = sourceBytes.Slice(rowIndex * sourceStride, outputStride);
            writer.Write(row);
        }

        writer.Flush();
        return memoryStream.ToArray();
    }
}
