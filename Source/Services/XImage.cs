using System;
using System.Runtime.InteropServices;

namespace ShadowLink.Services;

[StructLayout(LayoutKind.Sequential)]
internal struct XImage
{
    public Int32 Width;
    public Int32 Height;
    public Int32 XOffset;
    public Int32 Format;
    public IntPtr Data;
    public Int32 ByteOrder;
    public Int32 BitmapUnit;
    public Int32 BitmapBitOrder;
    public Int32 BitmapPad;
    public Int32 Depth;
    public Int32 BytesPerLine;
    public Int32 BitsPerPixel;
    public UIntPtr RedMask;
    public UIntPtr GreenMask;
    public UIntPtr BlueMask;
    public IntPtr ObData;
}
