using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ShadowLink.Core.Models;

namespace ShadowLink.Services;

internal sealed class WindowsControlCaptureHook : IDisposable
{
    private const Int32 WhKeyboardLl = 13;
    private const Int32 WmKeyDown = 0x0100;
    private const Int32 WmKeyUp = 0x0101;
    private const Int32 WmSysKeyDown = 0x0104;
    private const Int32 WmSysKeyUp = 0x0105;

    private readonly String _displayId;
    private readonly Func<RemoteInputEvent, Task> _inputSink;
    private readonly Action _releaseAction;
    private readonly HookProc _hookProc;
    private readonly HashSet<String> _pressedKeys;
    private readonly UInt16 _releaseVirtualKey;
    private readonly UInt16 _emergencyVirtualKey;
    private readonly Boolean _useControlModifier;
    private readonly Boolean _useAltModifier;
    private readonly Boolean _useShiftModifier;
    private readonly Boolean _useMetaModifier;
    private IntPtr _hookHandle;
    private Boolean _isStarted;

    public WindowsControlCaptureHook(String displayId, String releaseGesture, Func<RemoteInputEvent, Task> inputSink, Action releaseAction)
    {
        _displayId = displayId;
        _inputSink = inputSink;
        _releaseAction = releaseAction;
        _hookProc = HandleHook;
        _pressedKeys = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
        ParseGesture(releaseGesture, out _releaseVirtualKey, out _useControlModifier, out _useAltModifier, out _useShiftModifier, out _useMetaModifier);
        _emergencyVirtualKey = WindowsInputKeyMap.ResolveVirtualKey("Escape");
    }

    public Boolean IsActive => _isStarted;

    public void Start()
    {
        if (!OperatingSystem.IsWindows() || _isStarted)
        {
            return;
        }

        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, IntPtr.Zero, 0);
        if (_hookHandle != IntPtr.Zero)
        {
            _isStarted = true;
        }
    }

    public void Stop()
    {
        if (!_isStarted)
        {
            return;
        }

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _isStarted = false;
        ReleaseAllPressedKeys();
        _pressedKeys.Clear();
    }

    public void Dispose()
    {
        Stop();
    }

    private IntPtr HandleHook(Int32 code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || !_isStarted)
        {
            return CallNextHookEx(_hookHandle, code, wParam, lParam);
        }

        Int32 message = wParam.ToInt32();
        if (message != WmKeyDown && message != WmKeyUp && message != WmSysKeyDown && message != WmSysKeyUp)
        {
            return CallNextHookEx(_hookHandle, code, wParam, lParam);
        }

        KbdLlHookStruct hookStruct = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        if (!WindowsInputKeyMap.TryGetKeyName((Int32)hookStruct.VirtualKeyCode, out String keyName))
        {
            return CallNextHookEx(_hookHandle, code, wParam, lParam);
        }

        Boolean isKeyDown = message == WmKeyDown || message == WmSysKeyDown;
        Boolean isKeyUp = message == WmKeyUp || message == WmSysKeyUp;

        if (isKeyDown && IsReleaseGesture((UInt16)hookStruct.VirtualKeyCode))
        {
            Stop();
            _releaseAction();
            return (IntPtr)1;
        }

        if (isKeyDown)
        {
            _pressedKeys.Add(keyName);
            _ = _inputSink(new RemoteInputEvent
            {
                Kind = RemoteInputEventKind.KeyDown,
                DisplayId = _displayId,
                Key = keyName
            });
            return (IntPtr)1;
        }

        if (isKeyUp)
        {
            _pressedKeys.Remove(keyName);
            _ = _inputSink(new RemoteInputEvent
            {
                Kind = RemoteInputEventKind.KeyUp,
                DisplayId = _displayId,
                Key = keyName
            });
            return (IntPtr)1;
        }

        return CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    private void ReleaseAllPressedKeys()
    {
        foreach (String keyName in _pressedKeys)
        {
            _ = _inputSink(new RemoteInputEvent
            {
                Kind = RemoteInputEventKind.KeyUp,
                DisplayId = _displayId,
                Key = keyName
            });
        }
    }

    private Boolean IsReleaseGesture(UInt16 keyCode)
    {
        if (IsEmergencyRelease(keyCode))
        {
            return true;
        }

        if (_releaseVirtualKey == 0 || keyCode != _releaseVirtualKey)
        {
            return false;
        }

        return IsModifierStateSatisfied(_useControlModifier, 0x11) &&
               IsModifierStateSatisfied(_useAltModifier, 0x12) &&
               IsModifierStateSatisfied(_useShiftModifier, 0x10) &&
               IsModifierStateSatisfied(_useMetaModifier, 0x5B, 0x5C);
    }

    private static Boolean IsModifierStateSatisfied(Boolean required, params Int32[] virtualKeys)
    {
        if (!required)
        {
            return true;
        }

        foreach (Int32 virtualKey in virtualKeys)
        {
            if ((GetAsyncKeyState(virtualKey) & 0x8000) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private Boolean IsEmergencyRelease(UInt16 keyCode)
    {
        return keyCode == _emergencyVirtualKey &&
               IsModifierStateSatisfied(true, 0x11) &&
               IsModifierStateSatisfied(true, 0x10);
    }

    private static void ParseGesture(String gesture, out UInt16 keyCode, out Boolean useControl, out Boolean useAlt, out Boolean useShift, out Boolean useMeta)
    {
        keyCode = 0;
        useControl = false;
        useAlt = false;
        useShift = false;
        useMeta = false;

        String[] tokens = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            keyCode = WindowsInputKeyMap.ResolveVirtualKey("Backspace");
            useControl = true;
            useAlt = true;
            useShift = true;
            return;
        }

        for (Int32 index = 0; index < tokens.Length - 1; index++)
        {
            String token = tokens[index];
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || token.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                useControl = true;
            }
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                useAlt = true;
            }
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                useShift = true;
            }
            else if (token.Equals("Meta", StringComparison.OrdinalIgnoreCase) || token.Equals("Win", StringComparison.OrdinalIgnoreCase))
            {
                useMeta = true;
            }
        }

        keyCode = WindowsInputKeyMap.ResolveVirtualKey(tokens[^1]);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(Int32 idHook, HookProc lpfn, IntPtr hmod, UInt32 dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern Boolean UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, Int32 nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern Int16 GetAsyncKeyState(Int32 vKey);

}
