using System;
using System.IO;

namespace ShadowLink.Infrastructure.Session;

internal sealed class IncomingFileTransferState
{
    public IncomingFileTransferState(String filePath, String fileName, Int64 totalBytes, FileStream stream)
    {
        FilePath = filePath;
        FileName = fileName;
        TotalBytes = totalBytes;
        Stream = stream;
    }

    public String FilePath { get; }

    public String FileName { get; }

    public Int64 TotalBytes { get; }

    public Int64 BytesWritten { get; set; }

    public FileStream Stream { get; }
}
