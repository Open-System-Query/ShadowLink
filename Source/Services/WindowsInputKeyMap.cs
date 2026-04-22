using System;

namespace ShadowLink.Services;

internal static class WindowsInputKeyMap
{
    public static UInt16 ResolveVirtualKey(String keyName)
    {
        if (String.IsNullOrWhiteSpace(keyName))
        {
            return 0;
        }

        if (keyName.Length == 1)
        {
            Char value = keyName[0];
            if (Char.IsLetterOrDigit(value))
            {
                return value;
            }
        }

        return keyName switch
        {
            "Back" or "Backspace" => 0x08,
            "Tab" => 0x09,
            "Enter" or "Return" => 0x0D,
            "Escape" => 0x1B,
            "Space" => 0x20,
            "Left" => 0x25,
            "Up" => 0x26,
            "Right" => 0x27,
            "Down" => 0x28,
            "Delete" => 0x2E,
            "Insert" => 0x2D,
            "Home" => 0x24,
            "End" => 0x23,
            "PageUp" or "Prior" => 0x21,
            "PageDown" or "Next" => 0x22,
            "CapsLock" => 0x14,
            "PrintScreen" or "Snapshot" => 0x2C,
            "Pause" => 0x13,
            "Apps" => 0x5D,
            "LWin" or "LeftMeta" => 0x5B,
            "RWin" or "RightMeta" or "Meta" => 0x5C,
            "LShift" or "LeftShift" => 0xA0,
            "RShift" or "RightShift" => 0xA1,
            "Shift" => 0x10,
            "LCtrl" or "LeftCtrl" => 0xA2,
            "RCtrl" or "RightCtrl" => 0xA3,
            "Ctrl" or "Control" => 0x11,
            "LAlt" or "LeftAlt" => 0xA4,
            "RAlt" or "RightAlt" => 0xA5,
            "Alt" => 0x12,
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            _ => 0
        };
    }

    public static Boolean TryGetKeyName(Int32 virtualKey, out String keyName)
    {
        if (virtualKey >= 0x30 && virtualKey <= 0x39)
        {
            keyName = ((Char)virtualKey).ToString();
            return true;
        }

        if (virtualKey >= 0x41 && virtualKey <= 0x5A)
        {
            keyName = ((Char)virtualKey).ToString();
            return true;
        }

        keyName = virtualKey switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Escape",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x5B => "LWin",
            0x5C => "RWin",
            0x5D => "Apps",
            0xA0 => "LeftShift",
            0xA1 => "RightShift",
            0xA2 => "LeftCtrl",
            0xA3 => "RightCtrl",
            0xA4 => "LeftAlt",
            0xA5 => "RightAlt",
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            _ => String.Empty
        };

        return !String.IsNullOrWhiteSpace(keyName);
    }
}
