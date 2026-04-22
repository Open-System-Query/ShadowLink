using System;
using System.Runtime.InteropServices;

namespace ShadowLink.Services;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal struct MonitorInfoEx
{
    public Int32 Size;
    public RECT Monitor;
    public RECT WorkArea;
    public UInt32 Flags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public String DeviceName;
}
