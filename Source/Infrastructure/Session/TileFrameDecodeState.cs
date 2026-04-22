using System;
using ShadowLink.Core.Models;

namespace ShadowLink.Infrastructure.Session;

internal sealed class TileFrameDecodeState
{
    public TileFrameDecodeState(Int32 frameWidth, Int32 frameHeight, Int32 tileSize, StreamColorMode colorMode, Int32 dictionarySizeMb, Int32 staticCodebookSharePercent)
    {
        FrameBuffer = new Byte[frameWidth * frameHeight * 4];
        FrameWidth = frameWidth;
        FrameHeight = frameHeight;
        TileSize = tileSize;
        ColorMode = colorMode;
        DictionarySizeMb = dictionarySizeMb;
        TileDictionaryBudget budget = TileDictionaryBudget.FromMegabytes(dictionarySizeMb, staticCodebookSharePercent);
        StaticCodebookSharePercent = budget.StaticCodebookSharePercent;
        StaticCodebookBudgetBytes = budget.StaticCodebookBytes;
        FullTileDictionary = new TileFrameDictionary(budget.FullTileBytes);
        PatchDictionary = new TileFrameDictionary(budget.PatchBytes);
        TopologyDictionary = new TileFrameDictionary(budget.StaticCodebookBytes);
    }

    public Int32 FrameWidth { get; }

    public Int32 FrameHeight { get; }

    public Int32 TileSize { get; }

    public StreamColorMode ColorMode { get; }

    public Int32 DictionarySizeMb { get; }

    public Int32 StaticCodebookSharePercent { get; }

    public Int64 StaticCodebookBudgetBytes { get; }

    public Byte[] FrameBuffer { get; }

    public TileFrameDictionary FullTileDictionary { get; }

    public TileFrameDictionary PatchDictionary { get; }

    public TileFrameDictionary TopologyDictionary { get; }
}
