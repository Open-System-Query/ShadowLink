namespace ShadowLink.Infrastructure.Serialization;

internal enum SessionPacketType : byte
{
    DisplayManifest = 1,
    DisplayFrame = 2,
    InputEvent = 3,
    Clipboard = 4,
    FileTransfer = 5
}
