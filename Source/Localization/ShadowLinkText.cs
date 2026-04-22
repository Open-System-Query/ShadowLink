using System;
using System.Globalization;

namespace ShadowLink.Localization;

public static class ShadowLinkText
{
    private static IShadowLinkLocalizationService? _service;

    public static void Initialize(IShadowLinkLocalizationService service)
    {
        _service = service;
    }

    public static String Translate(String key)
    {
        if (String.IsNullOrWhiteSpace(key))
        {
            return String.Empty;
        }

        return _service?.GetText(key) ?? key;
    }

    public static String TranslateOrOriginal(String value)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return String.Empty;
        }

        return value.IndexOf('.') >= 0
            ? Translate(value)
            : value;
    }

    public static String TranslateFormat(String key, params Object[] arguments)
    {
        return String.Format(CultureInfo.CurrentCulture, Translate(key), arguments);
    }
}
