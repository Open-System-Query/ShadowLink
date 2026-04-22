using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ShadowLink.Core.Contracts;
using ShadowLink.Core.Models;

namespace ShadowLink.Services;

public sealed class MacDesktopStreamHost : IDesktopStreamHost
{
    private const Int32 BitmapInfoByteOrder32LittlePremultipliedFirst = 0x2002;
    private const Int32 EventTapHid = 0;
    private const Int32 MouseButtonLeft = 0;
    private const Int32 MouseButtonRight = 1;
    private const Int32 MouseButtonCenter = 2;
    private const Int32 EventTypeMouseMoved = 5;
    private const Int32 EventTypeLeftMouseDown = 1;
    private const Int32 EventTypeLeftMouseUp = 2;
    private const Int32 EventTypeRightMouseDown = 3;
    private const Int32 EventTypeRightMouseUp = 4;
    private const Int32 EventTypeOtherMouseDown = 25;
    private const Int32 EventTypeOtherMouseUp = 26;
    private Int32 _streamWidth = 1280;
    private Int32 _streamHeight = 720;

    public Boolean IsSupported => OperatingSystem.IsMacOS();

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

        UInt32[] displayIds = new UInt32[16];
        Int32 result = CGGetActiveDisplayList((UInt32)displayIds.Length, displayIds, out UInt32 displayCount);
        if (result != 0 || displayCount == 0)
        {
            return Array.Empty<RemoteDisplayDescriptor>();
        }

        List<RemoteDisplayDescriptor> displays = new List<RemoteDisplayDescriptor>((Int32)displayCount);
        for (Int32 index = 0; index < displayCount; index++)
        {
            UInt32 displayId = displayIds[index];
            CGRect bounds = CGDisplayBounds(displayId);
            displays.Add(new RemoteDisplayDescriptor
            {
                DisplayId = displayId.ToString(),
                Name = "Display " + (index + 1),
                Left = (Int32)Math.Round(bounds.Origin.X),
                Top = (Int32)Math.Round(bounds.Origin.Y),
                Width = (Int32)Math.Round(bounds.Size.Width),
                Height = (Int32)Math.Round(bounds.Size.Height)
            });
        }

        return displays;
    }

    public CapturedDisplayFrame CaptureDisplayFrame(String displayId)
    {
        if (!IsSupported || !UInt32.TryParse(displayId, out UInt32 cgDisplayId))
        {
            return new CapturedDisplayFrame();
        }

        IntPtr image = CGDisplayCreateImage(cgDisplayId);
        if (image == IntPtr.Zero)
        {
            return new CapturedDisplayFrame();
        }

        Int32 width = (Int32)CGImageGetWidth(image);
        Int32 height = (Int32)CGImageGetHeight(image);
        Int32 bytesPerRow = width * 4;
        Byte[] buffer = new Byte[bytesPerRow * height];
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        IntPtr colorSpace = IntPtr.Zero;
        IntPtr context = IntPtr.Zero;

        try
        {
            colorSpace = CGColorSpaceCreateDeviceRGB();
            context = CGBitmapContextCreate(handle.AddrOfPinnedObject(), (nuint)width, (nuint)height, 8, (nuint)bytesPerRow, colorSpace, (UInt32)BitmapInfoByteOrder32LittlePremultipliedFirst);
            if (context == IntPtr.Zero)
            {
                return new CapturedDisplayFrame();
            }

            CGRect rect = new CGRect
            {
                Origin = new CGPoint { X = 0, Y = 0 },
                Size = new CGSize { Width = width, Height = height }
            };
            CGContextDrawImage(context, rect, image);
            (Int32 scaledWidth, Int32 scaledHeight, Byte[] scaledPixels, Int32 scaledStride) = BgraFrameScaler.ScaleToFit(width, height, buffer, bytesPerRow, _streamWidth, _streamHeight);
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
            if (context != IntPtr.Zero)
            {
                CGContextRelease(context);
            }

            if (colorSpace != IntPtr.Zero)
            {
                CGColorSpaceRelease(colorSpace);
            }

            CGImageRelease(image);
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
    }

    public void ApplyInput(RemoteInputEvent inputEvent)
    {
        if (!IsSupported || !UInt32.TryParse(inputEvent.DisplayId, out UInt32 cgDisplayId))
        {
            return;
        }

        CGRect bounds = CGDisplayBounds(cgDisplayId);
        Double absoluteX = bounds.Origin.X + (Math.Clamp(inputEvent.X, 0.0, 1.0) * Math.Max(0.0, bounds.Size.Width - 1.0));
        Double absoluteY = bounds.Origin.Y + (Math.Clamp(inputEvent.Y, 0.0, 1.0) * Math.Max(0.0, bounds.Size.Height - 1.0));
        CGPoint point = new CGPoint { X = absoluteX, Y = absoluteY };

        switch (inputEvent.Kind)
        {
            case RemoteInputEventKind.PointerMove:
                PostMouseEvent(EventTypeMouseMoved, point, ResolveMouseButton(inputEvent.Button));
                break;
            case RemoteInputEventKind.PointerDown:
                PostMouseEvent(ResolveMouseDownType(inputEvent.Button), point, ResolveMouseButton(inputEvent.Button));
                break;
            case RemoteInputEventKind.PointerUp:
                PostMouseEvent(ResolveMouseUpType(inputEvent.Button), point, ResolveMouseButton(inputEvent.Button));
                break;
            case RemoteInputEventKind.MouseWheel:
                IntPtr scrollEvent = CGEventCreateScrollWheelEvent(IntPtr.Zero, 0, 2, inputEvent.WheelDeltaY / 12, inputEvent.WheelDeltaX / 12);
                if (scrollEvent != IntPtr.Zero)
                {
                    CGEventPost(EventTapHid, scrollEvent);
                    CFRelease(scrollEvent);
                }
                break;
            case RemoteInputEventKind.KeyDown:
                PostKeyEvent(inputEvent.Key, true);
                break;
            case RemoteInputEventKind.KeyUp:
                PostKeyEvent(inputEvent.Key, false);
                break;
        }
    }

    private static void PostMouseEvent(Int32 eventType, CGPoint point, Int32 button)
    {
        IntPtr mouseEvent = CGEventCreateMouseEvent(IntPtr.Zero, eventType, point, button);
        if (mouseEvent == IntPtr.Zero)
        {
            return;
        }

        CGEventPost(EventTapHid, mouseEvent);
        CFRelease(mouseEvent);
    }

    private static void PostKeyEvent(String keyName, Boolean isDown)
    {
        UInt16 keyCode = ResolveMacKeyCode(keyName);
        if (keyCode == UInt16.MaxValue)
        {
            return;
        }

        IntPtr keyEvent = CGEventCreateKeyboardEvent(IntPtr.Zero, keyCode, isDown);
        if (keyEvent == IntPtr.Zero)
        {
            return;
        }

        CGEventPost(EventTapHid, keyEvent);
        CFRelease(keyEvent);
    }

    private static Int32 ResolveMouseButton(String button)
    {
        return button switch
        {
            "Right" => MouseButtonRight,
            "Middle" => MouseButtonCenter,
            _ => MouseButtonLeft
        };
    }

    private static Int32 ResolveMouseDownType(String button)
    {
        return button switch
        {
            "Right" => EventTypeRightMouseDown,
            "Middle" => EventTypeOtherMouseDown,
            _ => EventTypeLeftMouseDown
        };
    }

    private static Int32 ResolveMouseUpType(String button)
    {
        return button switch
        {
            "Right" => EventTypeRightMouseUp,
            "Middle" => EventTypeOtherMouseUp,
            _ => EventTypeLeftMouseUp
        };
    }

    private static UInt16 ResolveMacKeyCode(String keyName)
    {
        if (String.IsNullOrWhiteSpace(keyName))
        {
            return UInt16.MaxValue;
        }

        if (keyName.Length == 1)
        {
            Char value = Char.ToUpperInvariant(keyName[0]);
            return value switch
            {
                'A' => 0x00,
                'S' => 0x01,
                'D' => 0x02,
                'F' => 0x03,
                'H' => 0x04,
                'G' => 0x05,
                'Z' => 0x06,
                'X' => 0x07,
                'C' => 0x08,
                'V' => 0x09,
                'B' => 0x0B,
                'Q' => 0x0C,
                'W' => 0x0D,
                'E' => 0x0E,
                'R' => 0x0F,
                'Y' => 0x10,
                'T' => 0x11,
                '1' => 0x12,
                '2' => 0x13,
                '3' => 0x14,
                '4' => 0x15,
                '6' => 0x16,
                '5' => 0x17,
                '=' => 0x18,
                '9' => 0x19,
                '7' => 0x1A,
                '-' => 0x1B,
                '8' => 0x1C,
                '0' => 0x1D,
                ']' => 0x1E,
                'O' => 0x1F,
                'U' => 0x20,
                '[' => 0x21,
                'I' => 0x22,
                'P' => 0x23,
                'L' => 0x25,
                'J' => 0x26,
                '\'' => 0x27,
                'K' => 0x28,
                ';' => 0x29,
                '\\' => 0x2A,
                ',' => 0x2B,
                '/' => 0x2C,
                'N' => 0x2D,
                'M' => 0x2E,
                '.' => 0x2F,
                '`' => 0x32,
                _ => UInt16.MaxValue
            };
        }

        return keyName switch
        {
            "Back" or "Backspace" => 0x33,
            "Tab" => 0x30,
            "Enter" or "Return" => 0x24,
            "Escape" => 0x35,
            "Space" => 0x31,
            "Left" => 0x7B,
            "Right" => 0x7C,
            "Down" => 0x7D,
            "Up" => 0x7E,
            "Delete" => 0x75,
            "Shift" or "LeftShift" => 0x38,
            "RightShift" => 0x3C,
            "Ctrl" or "Control" or "LeftCtrl" => 0x3B,
            "RightCtrl" => 0x3E,
            "Alt" or "LeftAlt" => 0x3A,
            "RightAlt" => 0x3D,
            "Meta" or "LWin" or "LeftMeta" => 0x37,
            "RWin" or "RightMeta" => 0x36,
            "F1" => 0x7A,
            "F2" => 0x78,
            "F3" => 0x63,
            "F4" => 0x76,
            "F5" => 0x60,
            "F6" => 0x61,
            "F7" => 0x62,
            "F8" => 0x64,
            "F9" => 0x65,
            "F10" => 0x6D,
            "F11" => 0x67,
            "F12" => 0x6F,
            _ => UInt16.MaxValue
        };
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern Int32 CGGetActiveDisplayList(UInt32 maxDisplays, [Out] UInt32[] activeDisplays, out UInt32 displayCount);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern CGRect CGDisplayBounds(UInt32 display);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGDisplayCreateImage(UInt32 display);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern nuint CGImageGetWidth(IntPtr image);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern nuint CGImageGetHeight(IntPtr image);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGColorSpaceCreateDeviceRGB();

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGBitmapContextCreate(IntPtr data, nuint width, nuint height, nuint bitsPerComponent, nuint bytesPerRow, IntPtr colorSpace, UInt32 bitmapInfo);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGContextDrawImage(IntPtr context, CGRect rect, IntPtr image);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGContextRelease(IntPtr context);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGColorSpaceRelease(IntPtr colorSpace);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreateMouseEvent(IntPtr source, Int32 mouseType, CGPoint mouseCursorPosition, Int32 mouseButton);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreateScrollWheelEvent(IntPtr source, Int32 units, UInt32 wheelCount, Int32 wheel1, Int32 wheel2);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, UInt16 virtualKey, Boolean keyDown);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventPost(Int32 tap, IntPtr @event);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGImageRelease(IntPtr image);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr handle);

}
