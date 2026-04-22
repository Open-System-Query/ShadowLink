namespace ShadowLink.Infrastructure.Serialization;

internal enum FileTransferPacketKind
{
    RequestSelection = 0,
    BeginFile = 1,
    Chunk = 2,
    CompleteFile = 3,
    Abort = 4
}
