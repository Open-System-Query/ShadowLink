using System;

namespace ShadowLink.Core.Models;

public sealed class ShortcutBinding
{
    public String Name { get; set; } = String.Empty;

    public String Gesture { get; set; } = String.Empty;

    public String Description { get; set; } = String.Empty;

    public Boolean IsEnabled { get; set; }
}
