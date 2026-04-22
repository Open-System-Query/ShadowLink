using System;

namespace ShadowLink.Services;

internal static class BgraFrameScaler
{
    public static (Int32 Width, Int32 Height, Byte[] Pixels, Int32 Stride) ScaleToFit(Int32 width, Int32 height, Byte[] sourcePixels, Int32 sourceStride, Int32 targetWidth, Int32 targetHeight)
    {
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            return (width, height, sourcePixels, sourceStride);
        }

        Double widthScale = targetWidth / (Double)width;
        Double heightScale = targetHeight / (Double)height;
        Double scale = Math.Min(widthScale, heightScale);
        if (scale >= 1.0)
        {
            return (width, height, sourcePixels, sourceStride);
        }

        Int32 scaledWidth = Math.Max(1, (Int32)Math.Round(width * scale));
        Int32 scaledHeight = Math.Max(1, (Int32)Math.Round(height * scale));
        Int32 scaledStride = scaledWidth * 4;
        Byte[] scaledPixels = new Byte[scaledStride * scaledHeight];

        for (Int32 destinationY = 0; destinationY < scaledHeight; destinationY++)
        {
            Int32 sourceY = Math.Min(height - 1, (Int32)Math.Round(destinationY / scale));
            for (Int32 destinationX = 0; destinationX < scaledWidth; destinationX++)
            {
                Int32 sourceX = Math.Min(width - 1, (Int32)Math.Round(destinationX / scale));
                Int32 sourceOffset = (sourceY * sourceStride) + (sourceX * 4);
                Int32 destinationOffset = (destinationY * scaledStride) + (destinationX * 4);
                scaledPixels[destinationOffset] = sourcePixels[sourceOffset];
                scaledPixels[destinationOffset + 1] = sourcePixels[sourceOffset + 1];
                scaledPixels[destinationOffset + 2] = sourcePixels[sourceOffset + 2];
                scaledPixels[destinationOffset + 3] = sourcePixels[sourceOffset + 3];
            }
        }

        return (scaledWidth, scaledHeight, scaledPixels, scaledStride);
    }
}
