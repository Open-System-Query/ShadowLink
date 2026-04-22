using System;

namespace ShadowLink.Infrastructure.Session;

internal readonly struct TileDictionaryBudget
{
    private TileDictionaryBudget(Int32 staticCodebookSharePercent, Int64 staticCodebookBytes, Int64 runtimeDictionaryBytes, Int64 fullTileBytes, Int64 patchBytes)
    {
        StaticCodebookSharePercent = staticCodebookSharePercent;
        StaticCodebookBytes = staticCodebookBytes;
        RuntimeDictionaryBytes = runtimeDictionaryBytes;
        FullTileBytes = fullTileBytes;
        PatchBytes = patchBytes;
    }

    public Int32 StaticCodebookSharePercent { get; }

    public Int64 StaticCodebookBytes { get; }

    public Int64 RuntimeDictionaryBytes { get; }

    public Int64 FullTileBytes { get; }

    public Int64 PatchBytes { get; }

    public static TileDictionaryBudget FromMegabytes(Int32 dictionarySizeMb, Int32 staticCodebookSharePercent)
    {
        Int64 totalBytes = Math.Max(64L * 1024L * 1024L, (Int64)dictionarySizeMb * 1024L * 1024L);
        Int32 clampedStaticPercent = Math.Clamp(staticCodebookSharePercent, 5, 95);
        Int64 minimumSideBytes = 16L * 1024L * 1024L;
        Int64 staticCodebookBytes = (totalBytes * clampedStaticPercent) / 100L;
        staticCodebookBytes = Math.Clamp(staticCodebookBytes, minimumSideBytes, totalBytes - minimumSideBytes);
        Int64 runtimeDictionaryBytes = Math.Max(minimumSideBytes, totalBytes - staticCodebookBytes);
        Int64 fullTileBytes = runtimeDictionaryBytes >= 256L * 1024L * 1024L
            ? (runtimeDictionaryBytes * 55L) / 100L
            : (runtimeDictionaryBytes * 3L) / 4L;
        Int64 patchBytes = runtimeDictionaryBytes - fullTileBytes;
        return new TileDictionaryBudget(clampedStaticPercent, staticCodebookBytes, runtimeDictionaryBytes, fullTileBytes, patchBytes);
    }
}
