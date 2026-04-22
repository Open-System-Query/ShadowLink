using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ShadowLink.Core.Contracts;
using ShadowLink.Core.Models;

namespace ShadowLink.Services;

public sealed class WindowsDesktopStreamHost : IDesktopStreamHost
{
    private const Int32 Srccopy = 0x00CC0020;
    private const Int32 CaptureBlt = 0x40000000;
    private const UInt32 InputMouse = 0;
    private const UInt32 InputKeyboard = 1;
    private const UInt32 MouseEventfLeftDown = 0x0002;
    private const UInt32 MouseEventfLeftUp = 0x0004;
    private const UInt32 MouseEventfRightDown = 0x0008;
    private const UInt32 MouseEventfRightUp = 0x0010;
    private const UInt32 MouseEventfMiddleDown = 0x0020;
    private const UInt32 MouseEventfMiddleUp = 0x0040;
    private const UInt32 MouseEventfWheel = 0x0800;
    private const UInt32 KeyEventfKeyUp = 0x0002;
    private const UInt32 DibRgbColors = 0;
    private const UInt32 BiRgb = 0;
    private IReadOnlyList<RemoteDisplayDescriptor> _cachedDisplays = Array.Empty<RemoteDisplayDescriptor>();
    private DateTimeOffset _lastDisplayRefreshUtc = DateTimeOffset.MinValue;
    private Int32 _streamWidth = 1280;
    private Int32 _streamHeight = 720;
    public Boolean IsSupported => OperatingSystem.IsWindows();

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

        if (_cachedDisplays.Count > 0 && DateTimeOffset.UtcNow - _lastDisplayRefreshUtc < TimeSpan.FromSeconds(2))
        {
            return _cachedDisplays;
        }

        List<RemoteDisplayDescriptor> displays = new List<RemoteDisplayDescriptor>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitorHandle, IntPtr deviceContext, ref RECT monitorRect, IntPtr data) =>
        {
            MonitorInfoEx monitorInfo = new MonitorInfoEx
            {
                Size = Marshal.SizeOf<MonitorInfoEx>()
            };

            if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
            {
                return true;
            }

            RECT bounds = monitorInfo.Monitor;
            displays.Add(new RemoteDisplayDescriptor
            {
                DisplayId = monitorInfo.DeviceName,
                Name = String.IsNullOrWhiteSpace(monitorInfo.DeviceName) ? "Display" : monitorInfo.DeviceName,
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Right - bounds.Left,
                Height = bounds.Bottom - bounds.Top
            });
            return true;
        }, IntPtr.Zero);

        _cachedDisplays = displays
            .OrderBy(item => item.Left)
            .ThenBy(item => item.Top)
            .ToArray();
        _lastDisplayRefreshUtc = DateTimeOffset.UtcNow;
        return _cachedDisplays;
    }

    public CapturedDisplayFrame CaptureDisplayFrame(String displayId)
    {
        if (!IsSupported)
        {
            return new CapturedDisplayFrame();
        }

        RemoteDisplayDescriptor? display = GetDisplays().FirstOrDefault(item => item.DisplayId.Equals(displayId, StringComparison.OrdinalIgnoreCase));
        if (display is null || display.Width <= 0 || display.Height <= 0)
        {
            return new CapturedDisplayFrame();
        }

        IntPtr desktopDc = GetDC(IntPtr.Zero);
        IntPtr memoryDc = CreateCompatibleDC(desktopDc);
        IntPtr bitmapHandle = CreateCompatibleBitmap(desktopDc, display.Width, display.Height);
        IntPtr previousObject = SelectObject(memoryDc, bitmapHandle);

        try
        {
            BitBlt(memoryDc, 0, 0, display.Width, display.Height, desktopDc, display.Left, display.Top, Srccopy | CaptureBlt);
            Int32 stride = display.Width * 4;
            Byte[] pixels = new Byte[stride * display.Height];
            BitmapInfo bitmapInfo = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (UInt32)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = display.Width,
                    Height = -display.Height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb,
                    SizeImage = (UInt32)pixels.Length
                }
            };

            Int32 scanLines = GetDIBits(memoryDc, bitmapHandle, 0, (UInt32)display.Height, pixels, ref bitmapInfo, DibRgbColors);
            if (scanLines == 0)
            {
                return new CapturedDisplayFrame();
            }

            (Int32 scaledWidth, Int32 scaledHeight, Byte[] scaledPixels, Int32 scaledStride) = BgraFrameScaler.ScaleToFit(display.Width, display.Height, pixels, stride, _streamWidth, _streamHeight);
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
            SelectObject(memoryDc, previousObject);
            DeleteObject(bitmapHandle);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, desktopDc);
        }
    }

    public void ApplyInput(RemoteInputEvent inputEvent)
    {
        if (!IsSupported)
        {
            return;
        }

        RemoteDisplayDescriptor? display = GetDisplays().FirstOrDefault(item => item.DisplayId.Equals(inputEvent.DisplayId, StringComparison.OrdinalIgnoreCase));
        if (display is not null)
        {
            Int32 x = display.Left + (Int32)Math.Round(Math.Clamp(inputEvent.X, 0.0, 1.0) * Math.Max(0, display.Width - 1));
            Int32 y = display.Top + (Int32)Math.Round(Math.Clamp(inputEvent.Y, 0.0, 1.0) * Math.Max(0, display.Height - 1));
            SetCursorPos(x, y);
        }

        switch (inputEvent.Kind)
        {
            case RemoteInputEventKind.PointerDown:
                SendMouseInput(MapMouseButtonDown(inputEvent.Button), 0);
                break;
            case RemoteInputEventKind.PointerUp:
                SendMouseInput(MapMouseButtonUp(inputEvent.Button), 0);
                break;
            case RemoteInputEventKind.MouseWheel:
                if (inputEvent.WheelDeltaY != 0)
                {
                    SendMouseInput(MouseEventfWheel, inputEvent.WheelDeltaY);
                }
                break;
            case RemoteInputEventKind.KeyDown:
                SendKeyboardInput(inputEvent.Key, false);
                break;
            case RemoteInputEventKind.KeyUp:
                SendKeyboardInput(inputEvent.Key, true);
                break;
        }
    }

    private static void SendMouseInput(UInt32 flags, Int32 mouseData)
    {
        if (flags == 0)
        {
            return;
        }

        Input[] inputs =
        {
            new Input
            {
                Type = InputMouse,
                Union = new InputUnion
                {
                    Mouse = new MouseInput
                    {
                        Flags = flags,
                        MouseData = mouseData
                    }
                }
            }
        };

        SendInput((UInt32)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static void SendKeyboardInput(String keyName, Boolean isKeyUp)
    {
        UInt16 virtualKey = WindowsInputKeyMap.ResolveVirtualKey(keyName);
        if (virtualKey == 0)
        {
            return;
        }

        Input[] inputs =
        {
            new Input
            {
                Type = InputKeyboard,
                Union = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = virtualKey,
                        Flags = isKeyUp ? KeyEventfKeyUp : 0
                    }
                }
            }
        };

        SendInput((UInt32)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static UInt32 MapMouseButtonDown(String button)
    {
        return button switch
        {
            "Left" => MouseEventfLeftDown,
            "Right" => MouseEventfRightDown,
            "Middle" => MouseEventfMiddleDown,
            _ => 0
        };
    }

    private static UInt32 MapMouseButtonUp(String button)
    {
        return button switch
        {
            "Left" => MouseEventfLeftUp,
            "Right" => MouseEventfRightUp,
            "Middle" => MouseEventfMiddleUp,
            _ => 0
        };
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern Int32 ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern Boolean DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, Int32 width, Int32 height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    private static extern Boolean DeleteObject(IntPtr graphicsObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern Boolean BitBlt(IntPtr destinationDc, Int32 x, Int32 y, Int32 width, Int32 height, IntPtr sourceDc, Int32 sourceX, Int32 sourceY, Int32 rop);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern Int32 GetDIBits(IntPtr deviceContext, IntPtr bitmapHandle, UInt32 startScan, UInt32 scanLines, [Out] Byte[] bits, ref BitmapInfo bitmapInfo, UInt32 usage);

    [DllImport("user32.dll")]
    private static extern Boolean EnumDisplayMonitors(IntPtr deviceContext, IntPtr clipRectangle, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern Boolean GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfoEx monitorInfo);

    [DllImport("user32.dll")]
    private static extern Boolean SetCursorPos(Int32 x, Int32 y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern UInt32 SendInput(UInt32 numberOfInputs, Input[] inputs, Int32 sizeOfInputStructure);

}
