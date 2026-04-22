using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace ShadowLink;

internal static class StartupControlElevation
{
    public static Boolean HasHandledStartupRequest { get; private set; }

    public static Boolean TryHandleStartupElevation(String[] args)
    {
        HasHandledStartupRequest = true;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (IsCurrentProcessElevated() || IsCurrentProcessUiAccessEnabled())
        {
            return false;
        }

        String? processPath = Environment.ProcessPath;
        if (String.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(processPath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };

            if (args.Length > 0)
            {
                startInfo.Arguments = String.Join(" ", args.Select(QuoteArgument));
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static Boolean IsCurrentProcessElevated()
    {
        WindowsIdentity? identity = WindowsIdentity.GetCurrent();
        if (identity is null)
        {
            return false;
        }

        WindowsPrincipal principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    [SupportedOSPlatform("windows")]
    private static Boolean IsCurrentProcessUiAccessEnabled()
    {
        IntPtr processHandle = NativeMethods.GetCurrentProcess();
        if (!NativeMethods.OpenProcessToken(processHandle, NativeMethods.TokenQuery, out IntPtr tokenHandle))
        {
            return false;
        }

        try
        {
            IntPtr buffer = Marshal.AllocHGlobal(sizeof(Int32));
            try
            {
                if (!NativeMethods.GetTokenInformation(tokenHandle, TokenInformationClass.TokenUIAccess, buffer, sizeof(Int32), out Int32 returnLength) ||
                    returnLength < sizeof(Int32))
                {
                    return false;
                }

                return Marshal.ReadInt32(buffer) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(tokenHandle);
        }
    }

    private static String QuoteArgument(String value)
    {
        return value.Contains(' ') ? "\"" + value.Replace("\"", "\\\"") + "\"" : value;
    }

    private enum TokenInformationClass
    {
        TokenUIAccess = 26
    }

    [SupportedOSPlatform("windows")]
    private static partial class NativeMethods
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
}
