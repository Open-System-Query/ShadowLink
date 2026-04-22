using System;

namespace ShadowLink.Infrastructure.Session;

internal sealed class TileFrameEncodeState
{
    public TileFrameEncodeState(Int32 dictionarySizeMb, Int32 staticCodebookSharePercent)
    {
        TileHashes = Array.Empty<TileDictionaryKey>();
        TileDictionaryBudget budget = TileDictionaryBudget.FromMegabytes(dictionarySizeMb, staticCodebookSharePercent);
        StaticCodebookSharePercent = budget.StaticCodebookSharePercent;
        StaticCodebookBudgetBytes = budget.StaticCodebookBytes;
        FullTileDictionary = new TileFrameDictionary(budget.FullTileBytes);
        PatchDictionary = new TileFrameDictionary(budget.PatchBytes);
        TopologyDictionary = new TileFrameDictionary(budget.StaticCodebookBytes);
    }

    public TileDictionaryKey[] TileHashes { get; set; }

    public Int32 StaticCodebookSharePercent { get; }

    public Int64 StaticCodebookBudgetBytes { get; }

    public TileFrameDictionary FullTileDictionary { get; }

    public TileFrameDictionary PatchDictionary { get; }

    public TileFrameDictionary TopologyDictionary { get; }
}
