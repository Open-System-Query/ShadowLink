using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ShadowLink.Core.Contracts;
using ShadowLink.Core.Models;

namespace ShadowLink.Services;

public sealed class LinuxDesktopStreamHost : IDesktopStreamHost
{
    private const Int32 ZPixmap = 2;
    private Int32 _streamWidth = 1280;
    private Int32 _streamHeight = 720;

    public Boolean IsSupported => OperatingSystem.IsLinux() && !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));

    public void UpdateSettings(AppSettings settings)
    {
        _streamWidth = settings.StreamWidth;
        _streamHeight = settings.StreamHeight;
    }

    public IReadOnlyList<RemoteDisplayDescriptor> GetDisplays()
    {
        if (!IsSupported)
        {
            return Array.Empty<RemoteDisplayDescriptor>();
        }

        IntPtr display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
        {
            return Array.Empty<RemoteDisplayDescriptor>();
        }

        try
        {
            IReadOnlyList<RemoteDisplayDescriptor> displays = TryGetDisplaysFromXinerama(display);
            if (displays.Count > 0)
            {
                return displays;
            }

            Int32 screen = XDefaultScreen(display);
            Int32 width = XDisplayWidth(display, screen);
            Int32 height = XDisplayHeight(display, screen);
            return new[]
            {
                new RemoteDisplayDescriptor
                {
                    DisplayId = "x11-root",
                    Name = "Desktop",
                    Left = 0,
                    Top = 0,
                    Width = width,
                    Height = height
                }
            };
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    public CapturedDisplayFrame CaptureDisplayFrame(String displayId)
    {
        if (!IsSupported)
        {
            return new CapturedDisplayFrame();
        }

        IntPtr display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
        {
            return new CapturedDisplayFrame();
        }

        try
        {
            Int32 screen = XDefaultScreen(display);
            IntPtr root = XRootWindow(display, screen);
            RemoteDisplayDescriptor? targetDisplay = GetDisplays().FirstOrDefault(item => item.DisplayId.Equals(displayId, StringComparison.OrdinalIgnoreCase));
            Int32 left = targetDisplay?.Left ?? 0;
            Int32 top = targetDisplay?.Top ?? 0;
            Int32 width = targetDisplay?.Width ?? XDisplayWidth(display, screen);
            Int32 height = targetDisplay?.Height ?? XDisplayHeight(display, screen);
            IntPtr imageHandle = XGetImage(display, root, left, top, (UInt32)width, (UInt32)height, UIntPtr.MaxValue, ZPixmap);
            if (imageHandle == IntPtr.Zero)
            {
                return new CapturedDisplayFrame();
            }

            try
            {
                XImage image = Marshal.PtrToStructure<XImage>(imageHandle);
                if (image.Data == IntPtr.Zero || image.BitsPerPixel < 24)
                {
                    return new CapturedDisplayFrame();
                }

                Byte[] pixels = new Byte[image.BytesPerLine * height];
                Marshal.Copy(image.Data, pixels, 0, pixels.Length);
                (Int32 scaledWidth, Int32 scaledHeight, Byte[] scaledPixels, Int32 scaledStride) = BgraFrameScaler.ScaleToFit(width, height, pixels, image.BytesPerLine, _streamWidth, _streamHeight);
                return new CapturedDisplayFrame
                {
                    Width = scaledWidth,
                    Height = scaledHeight,
                    Stride = scaledStride,
                    Pixels = scaledPixels
                };
            }
            finally
            {
                XDestroyImage(imageHandle);
            }
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    public void ApplyInput(RemoteInputEvent inputEvent)
    {
        if (!IsSupported)
        {
            return;
        }

        IntPtr display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Int32 screen = XDefaultScreen(display);
            RemoteDisplayDescriptor? targetDisplay = GetDisplays().FirstOrDefault(item => item.DisplayId.Equals(inputEvent.DisplayId, StringComparison.OrdinalIgnoreCase));
            Int32 width = targetDisplay?.Width ?? XDisplayWidth(display, screen);
            Int32 height = targetDisplay?.Height ?? XDisplayHeight(display, screen);
            Int32 x = (targetDisplay?.Left ?? 0) + (Int32)Math.Round(Math.Clamp(inputEvent.X, 0.0, 1.0) * Math.Max(0, width - 1));
            Int32 y = (targetDisplay?.Top ?? 0) + (Int32)Math.Round(Math.Clamp(inputEvent.Y, 0.0, 1.0) * Math.Max(0, height - 1));

            switch (inputEvent.Kind)
            {
                case RemoteInputEventKind.PointerMove:
                    XTestFakeMotionEvent(display, screen, x, y, 0);
                    break;
                case RemoteInputEventKind.PointerDown:
                    XTestFakeMotionEvent(display, screen, x, y, 0);
                    XTestFakeButtonEvent(display, ResolveButton(inputEvent.Button), true, 0);
                    break;
                case RemoteInputEventKind.PointerUp:
                    XTestFakeMotionEvent(display, screen, x, y, 0);
                    XTestFakeButtonEvent(display, ResolveButton(inputEvent.Button), false, 0);
                    break;
                case RemoteInputEventKind.MouseWheel:
                    EmitWheel(display, inputEvent.WheelDeltaY);
                    break;
                case RemoteInputEventKind.KeyDown:
                    EmitKey(display, inputEvent.Key, true);
                    break;
                case RemoteInputEventKind.KeyUp:
                    EmitKey(display, inputEvent.Key, false);
                    break;
            }

            XFlush(display);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static void EmitWheel(IntPtr display, Int32 deltaY)
    {
        Int32 steps = Math.Max(1, Math.Abs(deltaY) / 120);
        UInt32 button = deltaY >= 0 ? 4U : 5U;
        for (Int32 index = 0; index < steps; index++)
        {
            XTestFakeButtonEvent(display, button, true, 0);
            XTestFakeButtonEvent(display, button, false, 0);
        }
    }

    private static void EmitKey(IntPtr display, String keyName, Boolean isPressed)
    {
        IntPtr keyString = IntPtr.Zero;

        try
        {
            String keysymName = ResolveKeysymName(keyName);
            keyString = Marshal.StringToHGlobalAnsi(keysymName);
            UIntPtr keysym = XStringToKeysym(keyString);
            if (keysym == UIntPtr.Zero)
            {
                return;
            }

            Byte keycode = XKeysymToKeycode(display, keysym);
            if (keycode == 0)
            {
                return;
            }

            XTestFakeKeyEvent(display, keycode, isPressed, 0);
        }
        finally
        {
            if (keyString != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(keyString);
            }
        }
    }

    private static UInt32 ResolveButton(String button)
    {
        return button switch
        {
            "Right" => 3U,
            "Middle" => 2U,
            _ => 1U
        };
    }

    private static String ResolveKeysymName(String keyName)
    {
        if (String.IsNullOrWhiteSpace(keyName))
        {
            return "space";
        }

        if (keyName.Length == 1)
        {
            return keyName.ToLowerInvariant();
        }

        return keyName switch
        {
            "Back" or "Backspace" => "BackSpace",
            "Tab" => "Tab",
            "Enter" or "Return" => "Return",
            "Escape" => "Escape",
            "Space" => "space",
            "Left" => "Left",
            "Right" => "Right",
            "Up" => "Up",
            "Down" => "Down",
            "Delete" => "Delete",
            "Shift" or "LeftShift" or "RightShift" => "Shift_L",
            "Ctrl" or "Control" or "LeftCtrl" or "RightCtrl" => "Control_L",
            "Alt" or "LeftAlt" or "RightAlt" => "Alt_L",
            "Meta" or "LWin" or "RWin" or "LeftMeta" or "RightMeta" => "Super_L",
            "F1" => "F1",
            "F2" => "F2",
            "F3" => "F3",
            "F4" => "F4",
            "F5" => "F5",
            "F6" => "F6",
            "F7" => "F7",
            "F8" => "F8",
            "F9" => "F9",
            "F10" => "F10",
            "F11" => "F11",
            "F12" => "F12",
            _ => keyName
        };
    }

    private static IReadOnlyList<RemoteDisplayDescriptor> TryGetDisplaysFromXinerama(IntPtr display)
    {
        try
        {
            if (!XineramaIsActive(display))
            {
                return Array.Empty<RemoteDisplayDescriptor>();
            }

            IntPtr screens = XineramaQueryScreens(display, out Int32 screenCount);
            if (screens == IntPtr.Zero || screenCount <= 0)
            {
                return Array.Empty<RemoteDisplayDescriptor>();
            }

            try
            {
                List<RemoteDisplayDescriptor> displays = new List<RemoteDisplayDescriptor>(screenCount);
                Int32 screenInfoSize = Marshal.SizeOf<XineramaScreenInfo>();
                for (Int32 index = 0; index < screenCount; index++)
                {
                    XineramaScreenInfo screenInfo = Marshal.PtrToStructure<XineramaScreenInfo>(IntPtr.Add(screens, index * screenInfoSize));
                    displays.Add(new RemoteDisplayDescriptor
                    {
                        DisplayId = "x11-screen-" + screenInfo.ScreenNumber,
                        Name = "Display " + (index + 1),
                        Left = screenInfo.XOrg,
                        Top = screenInfo.YOrg,
                        Width = screenInfo.Width,
                        Height = screenInfo.Height
                    });
                }

                return displays;
            }
            finally
            {
                XFree(screens);
            }
        }
        catch (DllNotFoundException)
        {
            return Array.Empty<RemoteDisplayDescriptor>();
        }
        catch (EntryPointNotFoundException)
        {
            return Array.Empty<RemoteDisplayDescriptor>();
        }
    }

    [DllImport("libX11")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11")]
    private static extern Int32 XCloseDisplay(IntPtr display);

    [DllImport("libX11")]
    private static extern Int32 XDefaultScreen(IntPtr display);

    [DllImport("libX11")]
    private static extern Int32 XDisplayWidth(IntPtr display, Int32 screenNumber);

    [DllImport("libX11")]
    private static extern Int32 XDisplayHeight(IntPtr display, Int32 screenNumber);

    [DllImport("libX11")]
    private static extern IntPtr XRootWindow(IntPtr display, Int32 screenNumber);

    [DllImport("libX11")]
    private static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, Int32 x, Int32 y, UInt32 width, UInt32 height, UIntPtr planeMask, Int32 format);

    [DllImport("libX11")]
    private static extern Int32 XDestroyImage(IntPtr image);

    [DllImport("libX11")]
    private static extern Int32 XFlush(IntPtr display);

    [DllImport("libX11")]
    private static extern UIntPtr XStringToKeysym(IntPtr @string);

    [DllImport("libX11")]
    private static extern Byte XKeysymToKeycode(IntPtr display, UIntPtr keysym);

    [DllImport("libXtst")]
    private static extern Int32 XTestFakeMotionEvent(IntPtr display, Int32 screenNumber, Int32 x, Int32 y, UInt64 delay);

    [DllImport("libXtst")]
    private static extern Int32 XTestFakeButtonEvent(IntPtr display, UInt32 button, Boolean isPress, UInt64 delay);

    [DllImport("libXtst")]
    private static extern Int32 XTestFakeKeyEvent(IntPtr display, UInt32 keycode, Boolean isPress, UInt64 delay);

    [DllImport("libXinerama")]
    private static extern Boolean XineramaIsActive(IntPtr display);

    [DllImport("libXinerama")]
    private static extern IntPtr XineramaQueryScreens(IntPtr display, out Int32 number);

    [DllImport("libX11")]
    private static extern Int32 XFree(IntPtr data);

}
