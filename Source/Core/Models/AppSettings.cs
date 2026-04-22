using System;
using System.Collections.Generic;

namespace ShadowLink.Core.Models;

public sealed class AppSettings
{
    public String MachineId { get; set; } = Guid.NewGuid().ToString("N");

    public String DisplayName { get; set; } = Environment.MachineName;

    public ConnectionDirection DefaultDirection { get; set; } = ConnectionDirection.Receive;

    public TransportPreference PreferredTransport { get; set; } = TransportPreference.Auto;

    public Int32 DiscoveryPort { get; set; } = 45210;

    public Int32 ControlPort { get; set; } = 45211;

    public Int32 AutoRefreshIntervalSeconds { get; set; } = 3;

    public Int32 StreamWidth { get; set; } = 1280;

    public Int32 StreamHeight { get; set; } = 720;

    public Int32 StreamFrameRate { get; set; } = 30;

    public StreamColorMode StreamColorMode { get; set; } = StreamColorMode.Bgr24;

    public Int32 StreamTileSize { get; set; } = 16;

    public Int32 StreamDictionarySizeMb { get; set; } = 1024;

    public Int32 StreamStaticCodebookSharePercent { get; set; } = 50;

    public RemoteDisplayScaleMode DisplayScaleMode { get; set; } = RemoteDisplayScaleMode.ZoomToFit;

    public Boolean EnableKeyboardRelay { get; set; } = true;

    public Boolean EnableMouseRelay { get; set; } = true;

    public Boolean AutoStartDiscovery { get; set; } = true;

    public Boolean RememberRecentPeers { get; set; } = true;

    public String SessionPassphrase { get; set; } = String.Empty;

    public List<ShortcutBinding> ShortcutBindings { get; set; } = CreateDefaultShortcuts();

    public static AppSettings CreateDefault()
    {
        return new AppSettings();
    }

    private static List<ShortcutBinding> CreateDefaultShortcuts()
    {
        return new List<ShortcutBinding>
        {
            new ShortcutBinding
            {
                Name = "shortcut.release.name",
                Gesture = "Ctrl+Alt+Shift+Backspace",
                Description = "shortcut.release.description",
                IsEnabled = true
            },
            new ShortcutBinding
            {
                Name = "shortcut.quick_switch.name",
                Gesture = "Ctrl+Alt+Tab",
                Description = "shortcut.quick_switch.description",
                IsEnabled = true
            }
        };
    }
}
