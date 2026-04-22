using System;
using ShadowLink.Localization;

namespace ShadowLink.Core.Models;

public sealed class ActivityEntry
{
    public ActivityEntry(DateTimeOffset timestampUtc, String category, String message)
    {
        TimestampUtc = timestampUtc;
        Category = category;
        Message = message;
    }

    public DateTimeOffset TimestampUtc { get; }

    public String Category { get; }

    public String Message { get; }

    public String CategoryLabel => ShadowLinkText.TranslateOrOriginal(Category);

    public String MessageLabel => ShadowLinkText.TranslateOrOriginal(Message);

    public String TimestampLabel => TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
}
