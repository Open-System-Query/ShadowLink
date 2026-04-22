using System;

namespace ShadowLink.Infrastructure.Serialization;

internal sealed class FileTransferPacket
{
    public Guid TransferId { get; set; }

    public FileTransferPacketKind Kind { get; set; }

    public String FileName { get; set; } = String.Empty;

    public Int64 TotalBytes { get; set; }

    public Int32 ChunkIndex { get; set; }

    public String Message { get; set; } = String.Empty;
}
