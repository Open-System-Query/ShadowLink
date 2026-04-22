using System;

namespace ShadowLink.Infrastructure.Session;

internal readonly struct PatchEncodingCandidate
{
    public PatchEncodingCandidate(PatchFrameOperationKind kind, Byte[] payload, TileDictionaryEntryPayload? newTopologyEntry, Int32 referencedTopologyId)
    {
        Kind = kind;
        Payload = payload;
        NewTopologyEntry = newTopologyEntry;
        ReferencedTopologyId = referencedTopologyId;
    }

    public PatchFrameOperationKind Kind { get; }

    public Byte[] Payload { get; }

    public TileDictionaryEntryPayload? NewTopologyEntry { get; }

    public Int32 ReferencedTopologyId { get; }
}
