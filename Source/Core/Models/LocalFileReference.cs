using System;

namespace ShadowLink.Core.Models;

public sealed class LocalFileReference
{
    public String LocalPath { get; set; } = String.Empty;

    public String FileName { get; set; } = String.Empty;

    public Int64 Length { get; set; }
}
