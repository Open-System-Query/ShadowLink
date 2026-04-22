using System.Runtime.InteropServices;

namespace ShadowLink.Services;

[StructLayout(LayoutKind.Sequential)]
internal struct BitmapInfo
{
    public BitmapInfoHeader Header;
}
