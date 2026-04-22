using System;
using Avalonia.Controls;
using Avalonia.Platform;

namespace ShadowLink.Services;

internal static class AppWindowIconLoader
{
    private static readonly Lazy<WindowIcon?> Icon = new Lazy<WindowIcon?>(CreateIcon);

    public static WindowIcon? Load()
    {
        return Icon.Value;
    }

    private static WindowIcon? CreateIcon()
    {
        try
        {
            return new WindowIcon(AssetLoader.Open(new Uri("avares://ShadowLink/Assets/icon.png")));
        }
        catch
        {
            return null;
        }
    }
}
