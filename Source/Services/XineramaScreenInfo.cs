using System;
using System.Runtime.InteropServices;

namespace ShadowLink.Services;

[StructLayout(LayoutKind.Sequential)]
internal struct XineramaScreenInfo
{
    public Int32 ScreenNumber;
    public Int16 XOrg;
    public Int16 YOrg;
    public Int16 Width;
    public Int16 Height;
}
