using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ShadowLink.Core.Models;
using ShadowLink.Localization;

namespace ShadowLink.Services;

public sealed class RemoteDisplayWindow : Window
{
    private readonly RemoteDisplayDescriptor _displayDescriptor;
    private readonly Func<RemoteInputEvent, Task> _inputSink;
    private readonly Key _releaseKey;
    private readonly KeyModifiers _releaseModifiers;
    private readonly Image _image;
    private readonly WindowsControlCaptureHook? _windowsControlCaptureHook;
    private WriteableBitmap? _currentFrame;
    private IPointer? _capturedPointer;
    private Boolean _isInputCaptured;
    private DateTimeOffset _lastPointerMoveSentUtc;
    private Double _lastPointerMoveX = -1.0;
    private Double _lastPointerMoveY = -1.0;

    public RemoteDisplayWindow(RemoteDisplayDescriptor displayDescriptor, String releaseGesture, Func<RemoteInputEvent, Task> inputSink, ConnectionDirection direction, RemoteDisplayScaleMode displayScaleMode)
    {
        _displayDescriptor = displayDescriptor;
        _inputSink = inputSink;
        (_releaseKey, _releaseModifiers) = ParseReleaseGesture(releaseGesture);
        if (OperatingSystem.IsWindows() && direction == ConnectionDirection.Receive)
        {
            _windowsControlCaptureHook = new WindowsControlCaptureHook(displayDescriptor.DisplayId, releaseGesture, inputSink, () => Dispatcher.UIThread.Post(ReleaseInputCapture));
        }

        Title = BuildWindowTitle();
        Width = Math.Max(960, displayDescriptor.Width * 0.75);
        Height = Math.Max(600, displayDescriptor.Height * 0.75);
        MinWidth = 640;
        MinHeight = 360;
        Background = new SolidColorBrush(Color.Parse("#0F1720"));
        ClipToBounds = true;
        Icon = AppWindowIconLoader.Load();
        Focusable = true;
        UseLayoutRounding = true;
        AutomationProperties.SetName(this, ShadowLinkText.TranslateFormat("remote.window.automation", displayDescriptor.Name));

        _image = new Image
        {
            Stretch = ResolveStretch(displayScaleMode),
            StretchDirection = ResolveStretchDirection(displayScaleMode),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = true,
            TabIndex = 10,
            UseLayoutRounding = true
        };
        AutomationProperties.SetName(_image, ShadowLinkText.TranslateFormat("remote.image.automation", displayDescriptor.Name));
        RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.HighQuality);

        Content = _image;

        PointerPressed += HandlePointerPressed;
        PointerReleased += HandlePointerReleased;
        PointerMoved += HandlePointerMoved;
        PointerWheelChanged += HandlePointerWheelChanged;
        PointerExited += HandlePointerExited;
        KeyDown += HandleKeyDown;
        KeyUp += HandleKeyUp;
        Closed += HandleClosed;
    }

    public String DisplayId => _displayDescriptor.DisplayId;

    public void UpdateFrame(Byte[] framePixels, Int32 frameWidth, Int32 frameHeight, Int32 frameStride)
    {
        if (_currentFrame is null ||
            _currentFrame.PixelSize.Width != frameWidth ||
            _currentFrame.PixelSize.Height != frameHeight)
        {
            _currentFrame?.Dispose();
            _currentFrame = new WriteableBitmap(
                new PixelSize(frameWidth, frameHeight),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul);
            _image.Source = _currentFrame;
        }

        using ILockedFramebuffer lockedFramebuffer = _currentFrame.Lock();
        Int32 rowByteCount = Math.Min(frameStride, lockedFramebuffer.RowBytes);
        for (Int32 rowIndex = 0; rowIndex < frameHeight; rowIndex++)
        {
            IntPtr destination = IntPtr.Add(lockedFramebuffer.Address, rowIndex * lockedFramebuffer.RowBytes);
            Marshal.Copy(framePixels, rowIndex * frameStride, destination, rowByteCount);
        }

        _image.InvalidateVisual();
        InvalidateVisual();
    }

    private void HandlePointerPressed(Object? sender, PointerPressedEventArgs e)
    {
        EngageInputCapture();
        _capturedPointer = e.Pointer;
        e.Pointer.Capture(_image);
        FireAndForgetInput(SendPointerEventAsync(RemoteInputEventKind.PointerDown, e));
        e.Handled = true;
    }

    private void HandlePointerReleased(Object? sender, PointerReleasedEventArgs e)
    {
        if (!_isInputCaptured)
        {
            return;
        }

        FireAndForgetInput(SendPointerEventAsync(RemoteInputEventKind.PointerUp, e));
        e.Handled = true;
    }

    private void HandlePointerMoved(Object? sender, PointerEventArgs e)
    {
        if (!_isInputCaptured)
        {
            return;
        }

        Avalonia.Point position = e.GetPosition(_image);
        (Double normalizedX, Double normalizedY) = NormalizePosition(position);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (Math.Abs(normalizedX - _lastPointerMoveX) < 0.002 &&
            Math.Abs(normalizedY - _lastPointerMoveY) < 0.002 &&
            now - _lastPointerMoveSentUtc < TimeSpan.FromMilliseconds(12))
        {
            return;
        }

        _lastPointerMoveX = normalizedX;
        _lastPointerMoveY = normalizedY;
        _lastPointerMoveSentUtc = now;
        FireAndForgetInput(SendPointerEventAsync(RemoteInputEventKind.PointerMove, normalizedX, normalizedY, String.Empty));
    }

    private void HandlePointerWheelChanged(Object? sender, PointerWheelEventArgs e)
    {
        if (!_isInputCaptured)
        {
            return;
        }

        Avalonia.Point position = e.GetPosition(_image);
        (Double normalizedX, Double normalizedY) = NormalizePosition(position);
        RemoteInputEvent inputEvent = new RemoteInputEvent
        {
            Kind = RemoteInputEventKind.MouseWheel,
            DisplayId = _displayDescriptor.DisplayId,
            X = normalizedX,
            Y = normalizedY,
            WheelDeltaX = (Int32)Math.Round(e.Delta.X * 120.0),
            WheelDeltaY = (Int32)Math.Round(e.Delta.Y * 120.0)
        };

        FireAndForgetInput(_inputSink(inputEvent));
        e.Handled = true;
    }

    private void HandlePointerExited(Object? sender, PointerEventArgs e)
    {
        if (!_isInputCaptured)
        {
            return;
        }

        ReleaseInputCapture();
    }

    private void HandleKeyDown(Object? sender, KeyEventArgs e)
    {
        if (IsReleaseGesture(e))
        {
            ReleaseInputCapture();
            e.Handled = true;
            return;
        }

        if (_windowsControlCaptureHook?.IsActive == true)
        {
            e.Handled = true;
            return;
        }

        if (!_isInputCaptured)
        {
            return;
        }

        RemoteInputEvent inputEvent = new RemoteInputEvent
        {
            Kind = RemoteInputEventKind.KeyDown,
            DisplayId = _displayDescriptor.DisplayId,
            Key = e.Key.ToString()
        };

        FireAndForgetInput(_inputSink(inputEvent));
        e.Handled = true;
    }

    private void HandleKeyUp(Object? sender, KeyEventArgs e)
    {
        if (_windowsControlCaptureHook?.IsActive == true)
        {
            e.Handled = true;
            return;
        }

        if (!_isInputCaptured)
        {
            return;
        }

        RemoteInputEvent inputEvent = new RemoteInputEvent
        {
            Kind = RemoteInputEventKind.KeyUp,
            DisplayId = _displayDescriptor.DisplayId,
            Key = e.Key.ToString()
        };

        FireAndForgetInput(_inputSink(inputEvent));
        e.Handled = true;
    }

    private async Task SendPointerEventAsync(RemoteInputEventKind kind, PointerEventArgs pointerEventArgs)
    {
        Avalonia.Point position = pointerEventArgs.GetPosition(_image);
        (Double normalizedX, Double normalizedY) = NormalizePosition(position);
        await SendPointerEventAsync(kind, normalizedX, normalizedY, ResolveButton(pointerEventArgs.GetCurrentPoint(this).Properties)).ConfigureAwait(false);
    }

    private async Task SendPointerEventAsync(RemoteInputEventKind kind, Double normalizedX, Double normalizedY, String button)
    {
        RemoteInputEvent inputEvent = new RemoteInputEvent
        {
            Kind = kind,
            DisplayId = _displayDescriptor.DisplayId,
            X = normalizedX,
            Y = normalizedY,
            Button = button
        };

        await _inputSink(inputEvent).ConfigureAwait(false);
    }

    private (Double X, Double Y) NormalizePosition(Avalonia.Point position)
    {
        Double width = Math.Max(1.0, _image.Bounds.Width);
        Double height = Math.Max(1.0, _image.Bounds.Height);
        Double normalizedX = Math.Clamp(position.X / width, 0.0, 1.0);
        Double normalizedY = Math.Clamp(position.Y / height, 0.0, 1.0);
        return (normalizedX, normalizedY);
    }

    private Boolean IsReleaseGesture(KeyEventArgs e)
    {
        return e.Key == _releaseKey && (e.KeyModifiers & _releaseModifiers) == _releaseModifiers;
    }

    private static (Key Key, KeyModifiers Modifiers) ParseReleaseGesture(String gesture)
    {
        String[] tokens = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return (Key.Back, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift);
        }

        KeyModifiers modifiers = KeyModifiers.None;
        for (Int32 index = 0; index < tokens.Length - 1; index++)
        {
            String token = tokens[index];
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || token.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= KeyModifiers.Control;
            }
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= KeyModifiers.Alt;
            }
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= KeyModifiers.Shift;
            }
            else if (token.Equals("Meta", StringComparison.OrdinalIgnoreCase) || token.Equals("Win", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= KeyModifiers.Meta;
            }
        }

        String keyToken = NormalizeGestureKeyToken(tokens[tokens.Length - 1]);
        return Enum.TryParse(keyToken, true, out Key parsedKey)
            ? (parsedKey, modifiers)
            : (Key.Back, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift);
    }

    private static String NormalizeGestureKeyToken(String keyToken)
    {
        return keyToken.Trim() switch
        {
            "," => "OemComma",
            "Comma" => "OemComma",
            "." => "OemPeriod",
            "Period" => "OemPeriod",
            "/" => "OemQuestion",
            "?" => "OemQuestion",
            "Slash" => "OemQuestion",
            ";" => "OemSemicolon",
            ":" => "OemSemicolon",
            "Semicolon" => "OemSemicolon",
            "'" => "OemQuotes",
            "\"" => "OemQuotes",
            "Quote" => "OemQuotes",
            "[" => "OemOpenBrackets",
            "OpenBracket" => "OemOpenBrackets",
            "]" => "OemCloseBrackets",
            "CloseBracket" => "OemCloseBrackets",
            "\\" => "OemPipe",
            "|" => "OemPipe",
            "Backslash" => "OemPipe",
            "-" => "OemMinus",
            "_" => "OemMinus",
            "Minus" => "OemMinus",
            "=" => "OemPlus",
            "Equals" => "OemPlus",
            "`" => "OemTilde",
            "~" => "OemTilde",
            "Tilde" => "OemTilde",
            _ => keyToken.Trim()
        };
    }

    private String BuildWindowTitle()
    {
        return ShadowLinkText.TranslateFormat("remote.window.title", _displayDescriptor.Name);
    }

    private void EngageInputCapture()
    {
        _isInputCaptured = true;
        Topmost = true;
        Activate();
        Focus();
        _image.Focus();
        _windowsControlCaptureHook?.Start();
    }

    private void ReleaseInputCapture()
    {
        _isInputCaptured = false;
        Topmost = false;
        _capturedPointer?.Capture(null);
        _capturedPointer = null;
        _windowsControlCaptureHook?.Stop();
    }

    private static Stretch ResolveStretch(RemoteDisplayScaleMode displayScaleMode)
    {
        return displayScaleMode switch
        {
            RemoteDisplayScaleMode.Fill => Stretch.UniformToFill,
            _ => Stretch.Uniform
        };
    }

    private static StretchDirection ResolveStretchDirection(RemoteDisplayScaleMode displayScaleMode)
    {
        return displayScaleMode switch
        {
            RemoteDisplayScaleMode.ZoomToFit => StretchDirection.Both,
            RemoteDisplayScaleMode.Fill => StretchDirection.Both,
            _ => StretchDirection.DownOnly
        };
    }

    private static String ResolveButton(PointerPointProperties properties)
    {
        switch (properties.PointerUpdateKind)
        {
            case PointerUpdateKind.LeftButtonPressed:
            case PointerUpdateKind.LeftButtonReleased:
                return "Left";
            case PointerUpdateKind.RightButtonPressed:
            case PointerUpdateKind.RightButtonReleased:
                return "Right";
            case PointerUpdateKind.MiddleButtonPressed:
            case PointerUpdateKind.MiddleButtonReleased:
                return "Middle";
        }

        if (properties.IsLeftButtonPressed)
        {
            return "Left";
        }

        if (properties.IsRightButtonPressed)
        {
            return "Right";
        }

        if (properties.IsMiddleButtonPressed)
        {
            return "Middle";
        }

        return String.Empty;
    }

    private void HandleClosed(Object? sender, EventArgs e)
    {
        PointerPressed -= HandlePointerPressed;
        PointerReleased -= HandlePointerReleased;
        PointerMoved -= HandlePointerMoved;
        PointerWheelChanged -= HandlePointerWheelChanged;
        PointerExited -= HandlePointerExited;
        KeyDown -= HandleKeyDown;
        KeyUp -= HandleKeyUp;
        Closed -= HandleClosed;
        _windowsControlCaptureHook?.Dispose();
        _image.Source = null;
        _currentFrame?.Dispose();
        _currentFrame = null;
    }

    private static async void FireAndForgetInput(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }
}
