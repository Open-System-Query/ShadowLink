namespace ShadowLink.Infrastructure.Session;

internal enum PatchFrameOperationKind : byte
{
    RawPatch = 1,
    DictionaryReference = 2,
    TopologyDefinition = 3,
    TopologyReference = 4
}
