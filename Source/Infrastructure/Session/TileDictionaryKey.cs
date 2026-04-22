using System;
using System.Buffers.Binary;

namespace ShadowLink.Infrastructure.Session;

internal readonly struct TileDictionaryKey : IEquatable<TileDictionaryKey>
{
    private const UInt64 HashSeedA = 14695981039346656037UL;
    private const UInt64 HashSeedB = 1099511628211UL;
    private const UInt64 HashMixPrime = 11400714785074694791UL;

    public TileDictionaryKey(UInt64 high, UInt64 low)
    {
        High = high;
        Low = low;
    }

    public UInt64 High { get; }

    public UInt64 Low { get; }

    public static TileDictionaryKey FromBytes(ReadOnlySpan<Byte> bytes)
    {
        UInt64 high = HashSeedA ^ (UInt64)bytes.Length;
        UInt64 low = (HashSeedA >> 7) ^ ((UInt64)bytes.Length * HashSeedB);
        Int32 offset = 0;
        while (offset + 8 <= bytes.Length)
        {
            UInt64 chunk = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset, 8));
            high ^= chunk;
            high *= HashSeedB;
            low ^= RotateLeft(chunk + HashMixPrime, 23);
            low *= HashMixPrime;
            offset += 8;
        }

        while (offset < bytes.Length)
        {
            Byte value = bytes[offset++];
            high ^= value;
            high *= HashSeedB;
            low ^= (UInt64)(value + 97);
            low *= HashMixPrime;
        }

        high = Avalanche(high);
        low = Avalanche(low ^ RotateLeft(high, 17));
        return new TileDictionaryKey(high, low);
    }

    public Boolean Equals(TileDictionaryKey other)
    {
        return High == other.High && Low == other.Low;
    }

    public override Boolean Equals(Object? obj)
    {
        return obj is TileDictionaryKey other && Equals(other);
    }

    public override Int32 GetHashCode()
    {
        return HashCode.Combine(High, Low);
    }

    private static UInt64 RotateLeft(UInt64 value, Int32 count)
    {
        return (value << count) | (value >> (64 - count));
    }

    private static UInt64 Avalanche(UInt64 value)
    {
        value ^= value >> 33;
        value *= 0xff51afd7ed558ccdUL;
        value ^= value >> 33;
        value *= 0xc4ceb9fe1a85ec53UL;
        value ^= value >> 33;
        return value;
    }
}
