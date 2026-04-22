using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ShadowLink.Services;

[SupportedOSPlatform("windows")]
internal static partial class AppInteractionNativeMethods
{
    internal const UInt32 TokenQuery = 0x0008;

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern Boolean CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern Boolean OpenProcessToken(IntPtr processHandle, UInt32 desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern Boolean GetTokenInformation(IntPtr tokenHandle, TokenInformationClass tokenInformationClass, IntPtr tokenInformation, Int32 tokenInformationLength, out Int32 returnLength);
}
