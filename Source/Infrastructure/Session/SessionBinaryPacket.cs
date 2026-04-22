namespace ShadowLink.Infrastructure.Session;

internal sealed class SessionBinaryPacket
{
    public SessionBinaryPacket(byte packetType, byte[] metadata, byte[] payload)
    {
        PacketType = packetType;
        Metadata = metadata;
        Payload = payload;
    }

    public byte PacketType { get; }

    public byte[] Metadata { get; }

    public byte[] Payload { get; }
}
