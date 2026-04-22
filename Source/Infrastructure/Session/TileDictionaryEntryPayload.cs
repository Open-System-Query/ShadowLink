using System;

namespace ShadowLink.Infrastructure.Session;

internal readonly struct TileDictionaryEntryPayload
{
    public TileDictionaryEntryPayload(TileDictionaryKey key, Int32 dictionaryId, Byte[] bytes)
    {
        Key = key;
        DictionaryId = dictionaryId;
        Bytes = bytes;
    }

    public TileDictionaryKey Key { get; }

    public Int32 DictionaryId { get; }

    public Byte[] Bytes { get; }
}
