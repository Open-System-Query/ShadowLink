using System;
using System.Runtime.InteropServices;

namespace ShadowLink.Services;

[StructLayout(LayoutKind.Sequential)]
internal struct BitmapInfoHeader
{
    public UInt32 Size;
    public Int32 Width;
    public Int32 Height;
    public UInt16 Planes;
    public UInt16 BitCount;
    public UInt32 Compression;
    public UInt32 SizeImage;
    public Int32 XPelsPerMeter;
    public Int32 YPelsPerMeter;
    public UInt32 ClrUsed;
    public UInt32 ClrImportant;
}
