namespace ShadowLink.Infrastructure.Session;

internal enum TileFrameOperationKind : byte
{
    RawTile = 1,
    DictionaryReference = 2,
    SolidColorTile = 3,
    PaletteTile = 4,
    PatchComposition = 5
}
