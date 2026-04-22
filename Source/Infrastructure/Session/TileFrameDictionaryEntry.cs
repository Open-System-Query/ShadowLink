using System;
using System.Collections.Generic;

namespace ShadowLink.Infrastructure.Session;

internal sealed class TileFrameDictionaryEntry
{
    public TileFrameDictionaryEntry(Int32 dictionaryId, TileDictionaryKey key, Boolean hasKey, Byte[]? bytes, LinkedListNode<Int32>? node)
    {
        DictionaryId = dictionaryId;
        Key = key;
        HasKey = hasKey;
        Bytes = bytes;
        Node = node;
    }

    public Int32 DictionaryId { get; }

    public TileDictionaryKey Key { get; }

    public Boolean HasKey { get; }

    public Byte[]? Bytes { get; }

    public LinkedListNode<Int32>? Node { get; }

    public TileFrameDictionaryEntry WithNode(LinkedListNode<Int32> node)
    {
        return new TileFrameDictionaryEntry(DictionaryId, Key, HasKey, Bytes, node);
    }
}
