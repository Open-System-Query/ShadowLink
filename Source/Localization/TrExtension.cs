using System;
using Avalonia.Markup.Xaml;

namespace ShadowLink.Localization;

public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(String key)
    {
        Key = key;
    }

    public String Key { get; set; } = String.Empty;

    public override Object ProvideValue(IServiceProvider serviceProvider)
    {
        return ShadowLinkText.Translate(Key);
    }
}
