using System;
using System.Collections.Generic;

namespace ShadowLink.Infrastructure.Session;

internal readonly struct TileEncodingCandidate
{
    public TileEncodingCandidate(
        TileFrameOperationKind kind,
        Byte[] payload,
        Int32 dictionaryId,
        IReadOnlyList<TileDictionaryEntryPayload>? newPatchEntries = null,
        IReadOnlyList<Int32>? referencedPatchIds = null,
        IReadOnlyList<TileDictionaryEntryPayload>? newTopologyEntries = null,
        IReadOnlyList<Int32>? referencedTopologyIds = null)
    {
        Kind = kind;
        Payload = payload;
        DictionaryId = dictionaryId;
        NewPatchEntries = newPatchEntries ?? Array.Empty<TileDictionaryEntryPayload>();
        ReferencedPatchIds = referencedPatchIds ?? Array.Empty<Int32>();
        NewTopologyEntries = newTopologyEntries ?? Array.Empty<TileDictionaryEntryPayload>();
        ReferencedTopologyIds = referencedTopologyIds ?? Array.Empty<Int32>();
    }

    public TileFrameOperationKind Kind { get; }

    public Byte[] Payload { get; }

    public Int32 DictionaryId { get; }

    public IReadOnlyList<TileDictionaryEntryPayload> NewPatchEntries { get; }

    public IReadOnlyList<Int32> ReferencedPatchIds { get; }

    public IReadOnlyList<TileDictionaryEntryPayload> NewTopologyEntries { get; }

    public IReadOnlyList<Int32> ReferencedTopologyIds { get; }
}
