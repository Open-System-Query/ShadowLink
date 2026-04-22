using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using ShadowLink.Core.Models;
using ShadowLink.Infrastructure.Serialization;

namespace ShadowLink.Infrastructure.Session;

internal static class TileFrameCodec
{
    private const Int32 PreferredPatchSize = 8;
    private const Int32 MaxPaletteColors = 16;
    private const Int32 MinimumPatchCompositionRawBytes = 192;
    private const Int32 MinimumTopologyPatchRawBytes = 96;
    private static readonly UInt32[] Indexed4Palette =
    {
        0xFF000000,
        0xFF800000,
        0xFF008000,
        0xFF808000,
        0xFF000080,
        0xFF800080,
        0xFF008080,
        0xFFC0C0C0,
        0xFF808080,
        0xFFFF0000,
        0xFF00FF00,
        0xFFFFFF00,
        0xFF0000FF,
        0xFFFF00FF,
        0xFF00FFFF,
        0xFFFFFFFF
    };

    public static TileFrameEncodeResult EncodeFrame(CapturedDisplayFrame frame, AppSettings settings, TileFrameEncodeState state)
    {
        Int32 tileSize = Math.Max(4, settings.StreamTileSize);
        Int32 tileColumns = (frame.Width + tileSize - 1) / tileSize;
        Int32 tileRows = (frame.Height + tileSize - 1) / tileSize;
        Int32 tileCount = tileColumns * tileRows;

        if (state.TileHashes.Length != tileCount)
        {
            state.TileHashes = new TileDictionaryKey[tileCount];
        }

        using MemoryStream payloadStream = new MemoryStream(Math.Max(1024, frame.Width * frame.Height / 8));
        using BinaryWriter writer = new BinaryWriter(payloadStream);
        writer.Write(0);
        Int32 changedTileCount = 0;

        for (Int32 tileRow = 0; tileRow < tileRows; tileRow++)
        {
            for (Int32 tileColumn = 0; tileColumn < tileColumns; tileColumn++)
            {
                Int32 tileIndex = tileRow * tileColumns + tileColumn;
                Int32 startX = tileColumn * tileSize;
                Int32 startY = tileRow * tileSize;
                Int32 tileWidth = Math.Min(tileSize, frame.Width - startX);
                Int32 tileHeight = Math.Min(tileSize, frame.Height - startY);
                Byte[] encodedTile = EncodeTile(frame, settings.StreamColorMode, startX, startY, tileWidth, tileHeight);
                TileDictionaryKey tileKey = TileDictionaryKey.FromBytes(encodedTile);
                if (state.TileHashes[tileIndex].Equals(tileKey))
                {
                    continue;
                }

                state.TileHashes[tileIndex] = tileKey;
                TileEncodingCandidate candidate = BuildBestTileCandidate(encodedTile, tileKey, tileWidth, tileHeight, settings.StreamColorMode, state);
                CommitCandidate(candidate, tileKey, encodedTile, state);

                writer.Write(tileIndex);
                writer.Write((Byte)candidate.Kind);
                writer.Write(candidate.Payload.Length);
                writer.Write(candidate.Payload);
                changedTileCount++;
            }
        }

        payloadStream.Position = 0;
        writer.Write(changedTileCount);
        return new TileFrameEncodeResult(changedTileCount, payloadStream.ToArray());
    }

    public static Byte[] DecodeFrame(Byte[] payload, DisplayFramePacket packet, TileFrameDecodeState state)
    {
        Int32 tileColumns = (packet.FrameWidth + packet.TileSize - 1) / packet.TileSize;
        using MemoryStream payloadStream = new MemoryStream(payload, false);
        using BinaryReader reader = new BinaryReader(payloadStream);
        Int32 changedTileCount = reader.ReadInt32();
        Int32 bytesPerPixel = GetBytesPerPixel(packet.ColorMode);

        for (Int32 index = 0; index < changedTileCount; index++)
        {
            Int32 tileIndex = reader.ReadInt32();
            TileFrameOperationKind operationKind = (TileFrameOperationKind)reader.ReadByte();
            Int32 operationPayloadLength = reader.ReadInt32();
            Int32 tileColumn = tileIndex % tileColumns;
            Int32 tileRow = tileIndex / tileColumns;
            Int32 startX = tileColumn * packet.TileSize;
            Int32 startY = tileRow * packet.TileSize;
            Int32 tileWidth = Math.Min(packet.TileSize, packet.FrameWidth - startX);
            Int32 tileHeight = Math.Min(packet.TileSize, packet.FrameHeight - startY);
            Int32 tileByteCount = tileWidth * tileHeight * bytesPerPixel;
            if (operationPayloadLength < 0)
            {
                throw new InvalidDataException();
            }

            Byte[] operationPayload = reader.ReadBytes(operationPayloadLength);
            if (operationPayload.Length != operationPayloadLength)
            {
                throw new InvalidDataException();
            }

            using MemoryStream operationStream = new MemoryStream(operationPayload, false);
            using BinaryReader operationReader = new BinaryReader(operationStream);

            Byte[] encodedTile = operationKind switch
            {
                TileFrameOperationKind.DictionaryReference => DecodeDictionaryReference(operationReader, state),
                TileFrameOperationKind.RawTile => DecodeRawTile(operationReader, tileWidth, tileHeight, packet.ColorMode, state),
                TileFrameOperationKind.SolidColorTile => DecodeSolidTile(operationReader, tileWidth, tileHeight, bytesPerPixel, state),
                TileFrameOperationKind.PaletteTile => DecodePaletteTile(operationReader, tileWidth, tileHeight, bytesPerPixel, state),
                TileFrameOperationKind.PatchComposition => DecodePatchCompositionTile(operationReader, tileWidth, tileHeight, bytesPerPixel, state),
                _ => throw new InvalidDataException()
            };

            if (encodedTile.Length != tileByteCount)
            {
                throw new InvalidDataException();
            }

            DecodeTile(encodedTile, packet.ColorMode, state.FrameBuffer, packet.FrameWidth * 4, startX, startY, tileWidth, tileHeight);
        }

        return state.FrameBuffer;
    }

    private static TileEncodingCandidate BuildBestTileCandidate(Byte[] encodedTile, TileDictionaryKey tileKey, Int32 tileWidth, Int32 tileHeight, StreamColorMode colorMode, TileFrameEncodeState state)
    {
        if (state.FullTileDictionary.TryGetId(tileKey, out Int32 existingTileDictionaryId, touch: true))
        {
            return new TileEncodingCandidate(TileFrameOperationKind.DictionaryReference, SerializeDictionaryId(existingTileDictionaryId), existingTileDictionaryId);
        }

        Int32 tileDictionaryId = state.FullTileDictionary.ReserveId(tileKey);
        TileEncodingCandidate bestCandidate = CreateRawCandidate(tileDictionaryId, encodedTile, colorMode);

        if (TryCreateSolidCandidate(encodedTile, tileDictionaryId, colorMode, out TileEncodingCandidate solidCandidate) &&
            solidCandidate.Payload.Length < bestCandidate.Payload.Length)
        {
            bestCandidate = solidCandidate;
        }

        if (TryCreatePaletteCandidate(encodedTile, tileDictionaryId, colorMode, out TileEncodingCandidate paletteCandidate) &&
            paletteCandidate.Payload.Length < bestCandidate.Payload.Length)
        {
            bestCandidate = paletteCandidate;
        }

        if (TryCreatePatchCompositionCandidate(encodedTile, tileDictionaryId, tileWidth, tileHeight, colorMode, state, out TileEncodingCandidate patchCandidate) &&
            patchCandidate.Payload.Length < bestCandidate.Payload.Length)
        {
            bestCandidate = patchCandidate;
        }

        return bestCandidate;
    }

    private static TileEncodingCandidate CreateRawCandidate(Int32 dictionaryId, Byte[] encodedTile, StreamColorMode colorMode)
    {
        Byte[] wireEncodedTile = PackIndexed4BytesIfNeeded(encodedTile, colorMode);
        Byte[] payload = new Byte[4 + wireEncodedTile.Length];
        WriteDictionaryId(payload, dictionaryId);
        Buffer.BlockCopy(wireEncodedTile, 0, payload, 4, wireEncodedTile.Length);
        return new TileEncodingCandidate(TileFrameOperationKind.RawTile, payload, dictionaryId);
    }

    private static Boolean TryCreateSolidCandidate(Byte[] encodedTile, Int32 dictionaryId, StreamColorMode colorMode, out TileEncodingCandidate candidate)
    {
        Int32 bytesPerPixel = GetBytesPerPixel(colorMode);
        if (encodedTile.Length < bytesPerPixel || encodedTile.Length % bytesPerPixel != 0)
        {
            candidate = default;
            return false;
        }

        for (Int32 offset = bytesPerPixel; offset < encodedTile.Length; offset += bytesPerPixel)
        {
            if (!encodedTile.AsSpan(0, bytesPerPixel).SequenceEqual(encodedTile.AsSpan(offset, bytesPerPixel)))
            {
                candidate = default;
                return false;
            }
        }

        Byte[] payload = new Byte[4 + bytesPerPixel];
        WriteDictionaryId(payload, dictionaryId);
        Buffer.BlockCopy(encodedTile, 0, payload, 4, bytesPerPixel);
        candidate = new TileEncodingCandidate(TileFrameOperationKind.SolidColorTile, payload, dictionaryId);
        return true;
    }

    private static Boolean TryCreatePaletteCandidate(Byte[] encodedTile, Int32 dictionaryId, StreamColorMode colorMode, out TileEncodingCandidate candidate)
    {
        Int32 bytesPerPixel = GetBytesPerPixel(colorMode);
        if (bytesPerPixel <= 0 || encodedTile.Length == 0 || encodedTile.Length % bytesPerPixel != 0)
        {
            candidate = default;
            return false;
        }

        Dictionary<UInt32, Byte> paletteLookup = new Dictionary<UInt32, Byte>();
        List<UInt32> palette = new List<UInt32>();
        Int32 pixelCount = encodedTile.Length / bytesPerPixel;
        Byte[] indices = new Byte[pixelCount];

        for (Int32 pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
        {
            UInt32 pixelValue = ReadPixelValue(encodedTile, pixelIndex * bytesPerPixel, bytesPerPixel);
            if (!paletteLookup.TryGetValue(pixelValue, out Byte paletteIndex))
            {
                if (palette.Count >= MaxPaletteColors)
                {
                    candidate = default;
                    return false;
                }

                paletteIndex = (Byte)palette.Count;
                paletteLookup.Add(pixelValue, paletteIndex);
                palette.Add(pixelValue);
            }

            indices[pixelIndex] = paletteIndex;
        }

        if (palette.Count <= 1)
        {
            candidate = default;
            return false;
        }

        Int32 bitsPerIndex = palette.Count <= 2 ? 1 : palette.Count <= 4 ? 2 : 4;
        Byte[] packedIndices = PackPaletteIndices(indices, bitsPerIndex);
        Int32 paletteByteCount = palette.Count * bytesPerPixel;
        Byte[] payload = new Byte[4 + 1 + 1 + paletteByteCount + packedIndices.Length];
        WriteDictionaryId(payload, dictionaryId);
        payload[4] = (Byte)palette.Count;
        payload[5] = (Byte)bitsPerIndex;

        Int32 offset = 6;
        foreach (UInt32 paletteValue in palette)
        {
            WritePixelValue(payload, ref offset, paletteValue, bytesPerPixel);
        }

        Buffer.BlockCopy(packedIndices, 0, payload, offset, packedIndices.Length);
        candidate = new TileEncodingCandidate(TileFrameOperationKind.PaletteTile, payload, dictionaryId);
        return payload.Length < 4 + encodedTile.Length;
    }

    private static Boolean TryCreatePatchCompositionCandidate(Byte[] encodedTile, Int32 tileDictionaryId, Int32 tileWidth, Int32 tileHeight, StreamColorMode colorMode, TileFrameEncodeState state, out TileEncodingCandidate candidate)
    {
        if (encodedTile.Length < MinimumPatchCompositionRawBytes)
        {
            candidate = default;
            return false;
        }

        Int32 patchSize = Math.Min(PreferredPatchSize, Math.Min(tileWidth, tileHeight));
        if (patchSize < 4 || (tileWidth <= patchSize && tileHeight <= patchSize))
        {
            candidate = default;
            return false;
        }

        Int32 bytesPerPixel = GetBytesPerPixel(colorMode);
        Int32 patchColumns = (tileWidth + patchSize - 1) / patchSize;
        Int32 patchRows = (tileHeight + patchSize - 1) / patchSize;
        List<TileDictionaryEntryPayload> newPatchEntries = new List<TileDictionaryEntryPayload>();
        List<Int32> referencedPatchIds = new List<Int32>();
        List<TileDictionaryEntryPayload> newTopologyEntries = new List<TileDictionaryEntryPayload>();
        List<Int32> referencedTopologyIds = new List<Int32>();

        using MemoryStream payloadStream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(payloadStream);
        writer.Write(tileDictionaryId);
        writer.Write((Byte)patchSize);

        for (Int32 patchRow = 0; patchRow < patchRows; patchRow++)
        {
            for (Int32 patchColumn = 0; patchColumn < patchColumns; patchColumn++)
            {
                Int32 patchWidth = Math.Min(patchSize, tileWidth - (patchColumn * patchSize));
                Int32 patchHeight = Math.Min(patchSize, tileHeight - (patchRow * patchSize));
                Byte[] patchBytes = ExtractPatch(encodedTile, tileWidth, patchColumn * patchSize, patchRow * patchSize, patchWidth, patchHeight, bytesPerPixel);
                TileDictionaryKey patchKey = TileDictionaryKey.FromBytes(patchBytes);
                if (state.PatchDictionary.TryGetId(patchKey, out Int32 patchDictionaryId, touch: false))
                {
                    writer.Write((Byte)PatchFrameOperationKind.DictionaryReference);
                    writer.Write(patchDictionaryId);
                    referencedPatchIds.Add(patchDictionaryId);
                    continue;
                }

                patchDictionaryId = state.PatchDictionary.ReserveId(patchKey);
                if (patchBytes.Length >= MinimumTopologyPatchRawBytes &&
                    TryCreateTopologyPatchCandidate(patchBytes, patchWidth, patchHeight, bytesPerPixel, patchDictionaryId, state, out PatchEncodingCandidate topologyCandidate) &&
                    topologyCandidate.Payload.Length < 4 + patchBytes.Length)
                {
                    writer.Write((Byte)topologyCandidate.Kind);
                    writer.Write(topologyCandidate.Payload);
                    newPatchEntries.Add(new TileDictionaryEntryPayload(patchKey, patchDictionaryId, patchBytes));

                    if (topologyCandidate.NewTopologyEntry is TileDictionaryEntryPayload topologyEntry)
                    {
                        newTopologyEntries.Add(topologyEntry);
                    }

                    if (topologyCandidate.ReferencedTopologyId > 0)
                    {
                        referencedTopologyIds.Add(topologyCandidate.ReferencedTopologyId);
                    }

                    continue;
                }

                writer.Write((Byte)PatchFrameOperationKind.RawPatch);
                writer.Write(patchDictionaryId);
                writer.Write(patchBytes);
                newPatchEntries.Add(new TileDictionaryEntryPayload(patchKey, patchDictionaryId, patchBytes));
            }
        }

        Byte[] payload = payloadStream.ToArray();
        if (payload.Length >= 4 + encodedTile.Length)
        {
            candidate = default;
            return false;
        }

        candidate = new TileEncodingCandidate(
            TileFrameOperationKind.PatchComposition,
            payload,
            tileDictionaryId,
            newPatchEntries,
            referencedPatchIds,
            newTopologyEntries,
            referencedTopologyIds);
        return true;
    }

    private static void CommitCandidate(TileEncodingCandidate candidate, TileDictionaryKey tileKey, Byte[] encodedTile, TileFrameEncodeState state)
    {
        switch (candidate.Kind)
        {
            case TileFrameOperationKind.DictionaryReference:
                return;
            case TileFrameOperationKind.PatchComposition:
                foreach (Int32 patchDictionaryId in candidate.ReferencedPatchIds)
                {
                    state.PatchDictionary.TouchById(patchDictionaryId);
                }

                foreach (TileDictionaryEntryPayload patchEntry in candidate.NewPatchEntries)
                {
                    state.PatchDictionary.Add(patchEntry.Key, patchEntry.DictionaryId, patchEntry.Bytes);
                }

                foreach (Int32 topologyDictionaryId in candidate.ReferencedTopologyIds)
                {
                    state.TopologyDictionary.TouchById(topologyDictionaryId);
                }

                foreach (TileDictionaryEntryPayload topologyEntry in candidate.NewTopologyEntries)
                {
                    state.TopologyDictionary.Add(topologyEntry.Key, topologyEntry.DictionaryId, topologyEntry.Bytes);
                }

                break;
        }

        state.FullTileDictionary.Add(tileKey, candidate.DictionaryId, encodedTile);
    }

    private static Byte[] DecodeDictionaryReference(BinaryReader reader, TileFrameDecodeState state)
    {
        Int32 dictionaryId = reader.ReadInt32();
        if (!state.FullTileDictionary.TryGetById(dictionaryId, out Byte[]? encodedTile, touch: true))
        {
            throw new InvalidDataException();
        }

        return encodedTile!;
    }

    private static Byte[] DecodeRawTile(BinaryReader reader, Int32 tileWidth, Int32 tileHeight, StreamColorMode colorMode, TileFrameDecodeState state)
    {
        Int32 dictionaryId = reader.ReadInt32();
        Byte[] wireEncodedTile = reader.ReadBytes((Int32)(reader.BaseStream.Length - reader.BaseStream.Position));
        Byte[] encodedTile = UnpackIndexed4BytesIfNeeded(wireEncodedTile, colorMode, tileWidth, tileHeight);
        state.FullTileDictionary.AddKnown(dictionaryId, encodedTile);
        return encodedTile;
    }

    private static Byte[] DecodeSolidTile(BinaryReader reader, Int32 tileWidth, Int32 tileHeight, Int32 bytesPerPixel, TileFrameDecodeState state)
    {
        Int32 dictionaryId = reader.ReadInt32();
        Byte[] colorBytes = reader.ReadBytes(bytesPerPixel);
        if (colorBytes.Length != bytesPerPixel)
        {
            throw new InvalidDataException();
        }

        Byte[] encodedTile = BuildSolidTileBytes(colorBytes, tileWidth, tileHeight);
        state.FullTileDictionary.AddKnown(dictionaryId, encodedTile);
        return encodedTile;
    }

    private static Byte[] DecodePaletteTile(BinaryReader reader, Int32 tileWidth, Int32 tileHeight, Int32 bytesPerPixel, TileFrameDecodeState state)
    {
        Int32 dictionaryId = reader.ReadInt32();
        Int32 paletteCount = reader.ReadByte();
        Int32 bitsPerIndex = reader.ReadByte();
        if (paletteCount <= 1 || paletteCount > MaxPaletteColors || (bitsPerIndex != 1 && bitsPerIndex != 2 && bitsPerIndex != 4))
        {
            throw new InvalidDataException();
        }

        UInt32[] palette = new UInt32[paletteCount];
        for (Int32 paletteIndex = 0; paletteIndex < paletteCount; paletteIndex++)
        {
            Byte[] paletteBytes = reader.ReadBytes(bytesPerPixel);
            if (paletteBytes.Length != bytesPerPixel)
            {
                throw new InvalidDataException();
            }

            palette[paletteIndex] = ReadPixelValue(paletteBytes, 0, bytesPerPixel);
        }

        Byte[] packedIndices = reader.ReadBytes((Int32)(reader.BaseStream.Length - reader.BaseStream.Position));
        Byte[] indices = UnpackPaletteIndices(packedIndices, tileWidth * tileHeight, bitsPerIndex);
        Byte[] encodedTile = new Byte[tileWidth * tileHeight * bytesPerPixel];
        Int32 outputOffset = 0;
        foreach (Byte paletteIndex in indices)
        {
            if (paletteIndex >= palette.Length)
            {
                throw new InvalidDataException();
            }

            UInt32 paletteValue = palette[paletteIndex];
            WritePixelValue(encodedTile, ref outputOffset, paletteValue, bytesPerPixel);
        }

        state.FullTileDictionary.AddKnown(dictionaryId, encodedTile);
        return encodedTile;
    }

    private static Byte[] DecodePatchCompositionTile(BinaryReader reader, Int32 tileWidth, Int32 tileHeight, Int32 bytesPerPixel, TileFrameDecodeState state)
    {
        Int32 dictionaryId = reader.ReadInt32();
        Int32 patchSize = reader.ReadByte();
        if (patchSize < 4)
        {
            throw new InvalidDataException();
        }

        Int32 patchColumns = (tileWidth + patchSize - 1) / patchSize;
        Int32 patchRows = (tileHeight + patchSize - 1) / patchSize;
        Byte[] encodedTile = new Byte[tileWidth * tileHeight * bytesPerPixel];

        for (Int32 patchRow = 0; patchRow < patchRows; patchRow++)
        {
            for (Int32 patchColumn = 0; patchColumn < patchColumns; patchColumn++)
            {
                Int32 patchWidth = Math.Min(patchSize, tileWidth - (patchColumn * patchSize));
                Int32 patchHeight = Math.Min(patchSize, tileHeight - (patchRow * patchSize));
                Int32 patchByteCount = patchWidth * patchHeight * bytesPerPixel;
                PatchFrameOperationKind operationKind = (PatchFrameOperationKind)reader.ReadByte();
                Int32 patchDictionaryId = reader.ReadInt32();

                Byte[] patchBytes;
                if (operationKind == PatchFrameOperationKind.DictionaryReference)
                {
                    if (!state.PatchDictionary.TryGetById(patchDictionaryId, out patchBytes!, touch: true))
                    {
                        throw new InvalidDataException();
                    }
                }
                else if (operationKind == PatchFrameOperationKind.TopologyDefinition)
                {
                    Int32 topologyDictionaryId = reader.ReadInt32();
                    patchBytes = DecodeTopologyDefinitionPatch(reader, patchWidth, patchHeight, bytesPerPixel, out Byte[] topologyBytes);
                    state.TopologyDictionary.AddKnown(topologyDictionaryId, topologyBytes);
                    state.PatchDictionary.AddKnown(patchDictionaryId, patchBytes);
                }
                else if (operationKind == PatchFrameOperationKind.TopologyReference)
                {
                    Int32 topologyDictionaryId = reader.ReadInt32();
                    if (!state.TopologyDictionary.TryGetById(topologyDictionaryId, out Byte[]? topologyBytes, touch: true) || topologyBytes is null)
                    {
                        throw new InvalidDataException();
                    }

                    patchBytes = DecodeTopologyReferencePatch(reader, topologyBytes, patchWidth, patchHeight, bytesPerPixel);
                    state.PatchDictionary.AddKnown(patchDictionaryId, patchBytes);
                }
                else if (operationKind == PatchFrameOperationKind.RawPatch)
                {
                    patchBytes = reader.ReadBytes(patchByteCount);
                    if (patchBytes.Length != patchByteCount)
                    {
                        throw new InvalidDataException();
                    }

                    state.PatchDictionary.AddKnown(patchDictionaryId, patchBytes);
                }
                else
                {
                    throw new InvalidDataException();
                }

                CopyPatchToTile(encodedTile, tileWidth, patchColumn * patchSize, patchRow * patchSize, patchWidth, patchHeight, bytesPerPixel, patchBytes);
            }
        }

        state.FullTileDictionary.AddKnown(dictionaryId, encodedTile);
        return encodedTile;
    }

    private static Byte[] ExtractPatch(Byte[] encodedTile, Int32 tileWidth, Int32 startX, Int32 startY, Int32 patchWidth, Int32 patchHeight, Int32 bytesPerPixel)
    {
        Byte[] patchBytes = new Byte[patchWidth * patchHeight * bytesPerPixel];
        Int32 patchStride = patchWidth * bytesPerPixel;
        Int32 tileStride = tileWidth * bytesPerPixel;

        for (Int32 rowIndex = 0; rowIndex < patchHeight; rowIndex++)
        {
            Buffer.BlockCopy(
                encodedTile,
                ((startY + rowIndex) * tileStride) + (startX * bytesPerPixel),
                patchBytes,
                rowIndex * patchStride,
                patchStride);
        }

        return patchBytes;
    }

    private static void CopyPatchToTile(Byte[] encodedTile, Int32 tileWidth, Int32 startX, Int32 startY, Int32 patchWidth, Int32 patchHeight, Int32 bytesPerPixel, Byte[] patchBytes)
    {
        Int32 patchStride = patchWidth * bytesPerPixel;
        Int32 tileStride = tileWidth * bytesPerPixel;

        for (Int32 rowIndex = 0; rowIndex < patchHeight; rowIndex++)
        {
            Buffer.BlockCopy(
                patchBytes,
                rowIndex * patchStride,
                encodedTile,
                ((startY + rowIndex) * tileStride) + (startX * bytesPerPixel),
                patchStride);
        }
    }

    private static Byte[] BuildSolidTileBytes(Byte[] colorBytes, Int32 tileWidth, Int32 tileHeight)
    {
        Int32 bytesPerPixel = colorBytes.Length;
        Byte[] encodedTile = new Byte[tileWidth * tileHeight * bytesPerPixel];
        for (Int32 offset = 0; offset < encodedTile.Length; offset += bytesPerPixel)
        {
            Buffer.BlockCopy(colorBytes, 0, encodedTile, offset, bytesPerPixel);
        }

        return encodedTile;
    }

    private static UInt32 ReadPixelValue(Byte[] bytes, Int32 offset, Int32 bytesPerPixel)
    {
        UInt32 value = 0;
        for (Int32 byteIndex = 0; byteIndex < bytesPerPixel; byteIndex++)
        {
            value |= (UInt32)bytes[offset + byteIndex] << (byteIndex * 8);
        }

        return value;
    }

    private static void WritePixelValue(Byte[] buffer, ref Int32 offset, UInt32 value, Int32 bytesPerPixel)
    {
        for (Int32 byteIndex = 0; byteIndex < bytesPerPixel; byteIndex++)
        {
            buffer[offset++] = (Byte)(value >> (byteIndex * 8));
        }
    }

    private static Byte[] PackPaletteIndices(Byte[] indices, Int32 bitsPerIndex)
    {
        Int32 totalBits = indices.Length * bitsPerIndex;
        Byte[] packed = new Byte[(totalBits + 7) / 8];
        Int32 bitOffset = 0;
        foreach (Byte index in indices)
        {
            Int32 byteOffset = bitOffset / 8;
            Int32 intraByteOffset = bitOffset % 8;
            packed[byteOffset] |= (Byte)(index << intraByteOffset);
            if (intraByteOffset + bitsPerIndex > 8)
            {
                packed[byteOffset + 1] |= (Byte)(index >> (8 - intraByteOffset));
            }

            bitOffset += bitsPerIndex;
        }

        return packed;
    }

    private static Byte[] UnpackPaletteIndices(Byte[] packedIndices, Int32 pixelCount, Int32 bitsPerIndex)
    {
        Byte[] indices = new Byte[pixelCount];
        Int32 mask = (1 << bitsPerIndex) - 1;
        Int32 bitOffset = 0;
        for (Int32 pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
        {
            Int32 byteOffset = bitOffset / 8;
            Int32 intraByteOffset = bitOffset % 8;
            Int32 value = (packedIndices[byteOffset] >> intraByteOffset) & mask;
            if (intraByteOffset + bitsPerIndex > 8)
            {
                value |= (packedIndices[byteOffset + 1] << (8 - intraByteOffset)) & mask;
            }

            indices[pixelIndex] = (Byte)value;
            bitOffset += bitsPerIndex;
        }

        return indices;
    }

    private static Byte[] SerializeDictionaryId(Int32 dictionaryId)
    {
        Byte[] bytes = new Byte[4];
        WriteDictionaryId(bytes, dictionaryId);
        return bytes;
    }

    private static void WriteDictionaryId(Byte[] buffer, Int32 dictionaryId)
    {
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), dictionaryId);
    }

    private static Byte[] EncodeTile(CapturedDisplayFrame frame, StreamColorMode colorMode, Int32 startX, Int32 startY, Int32 tileWidth, Int32 tileHeight)
    {
        Int32 bytesPerPixel = GetBytesPerPixel(colorMode);
        Byte[] output = new Byte[tileWidth * tileHeight * bytesPerPixel];
        Int32 outputOffset = 0;

        for (Int32 y = 0; y < tileHeight; y++)
        {
            Int32 sourceOffset = (startY + y) * frame.Stride + (startX * 4);
            for (Int32 x = 0; x < tileWidth; x++)
            {
                Byte blue = frame.Pixels[sourceOffset];
                Byte green = frame.Pixels[sourceOffset + 1];
                Byte red = frame.Pixels[sourceOffset + 2];
                Byte alpha = frame.Pixels[sourceOffset + 3];

                switch (colorMode)
                {
                    case StreamColorMode.Bgra32:
                        output[outputOffset++] = blue;
                        output[outputOffset++] = green;
                        output[outputOffset++] = red;
                        output[outputOffset++] = alpha;
                        break;
                    case StreamColorMode.Bgr24:
                        output[outputOffset++] = blue;
                        output[outputOffset++] = green;
                        output[outputOffset++] = red;
                        break;
                    case StreamColorMode.Rgb565:
                        UInt16 rgb565 = (UInt16)(((red >> 3) << 11) | ((green >> 2) << 5) | (blue >> 3));
                        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(outputOffset, 2), rgb565);
                        outputOffset += 2;
                        break;
                    case StreamColorMode.Rgb332:
                        output[outputOffset++] = (Byte)((red & 0xE0) | ((green >> 3) & 0x1C) | (blue >> 6));
                        break;
                    case StreamColorMode.Indexed4:
                        output[outputOffset++] = FindNearestIndexed4ColorIndex(blue, green, red, alpha);
                        break;
                }

                sourceOffset += 4;
            }
        }

        return output;
    }

    private static void DecodeTile(ReadOnlySpan<Byte> encodedTile, StreamColorMode colorMode, Byte[] outputFrame, Int32 outputStride, Int32 startX, Int32 startY, Int32 tileWidth, Int32 tileHeight)
    {
        Int32 inputOffset = 0;

        for (Int32 y = 0; y < tileHeight; y++)
        {
            Int32 outputOffset = (startY + y) * outputStride + (startX * 4);
            for (Int32 x = 0; x < tileWidth; x++)
            {
                switch (colorMode)
                {
                    case StreamColorMode.Bgra32:
                        outputFrame[outputOffset] = encodedTile[inputOffset++];
                        outputFrame[outputOffset + 1] = encodedTile[inputOffset++];
                        outputFrame[outputOffset + 2] = encodedTile[inputOffset++];
                        outputFrame[outputOffset + 3] = encodedTile[inputOffset++];
                        break;
                    case StreamColorMode.Bgr24:
                        outputFrame[outputOffset] = encodedTile[inputOffset++];
                        outputFrame[outputOffset + 1] = encodedTile[inputOffset++];
                        outputFrame[outputOffset + 2] = encodedTile[inputOffset++];
                        outputFrame[outputOffset + 3] = 255;
                        break;
                    case StreamColorMode.Rgb565:
                        UInt16 rgb565 = BinaryPrimitives.ReadUInt16LittleEndian(encodedTile.Slice(inputOffset, 2));
                        inputOffset += 2;
                        outputFrame[outputOffset] = (Byte)((rgb565 & 0x1F) << 3);
                        outputFrame[outputOffset + 1] = (Byte)(((rgb565 >> 5) & 0x3F) << 2);
                        outputFrame[outputOffset + 2] = (Byte)(((rgb565 >> 11) & 0x1F) << 3);
                        outputFrame[outputOffset + 3] = 255;
                        break;
                    case StreamColorMode.Rgb332:
                        Byte rgb332 = encodedTile[inputOffset++];
                        outputFrame[outputOffset] = (Byte)((rgb332 & 0x03) * 85);
                        outputFrame[outputOffset + 1] = (Byte)(((rgb332 >> 2) & 0x07) * 36);
                        outputFrame[outputOffset + 2] = (Byte)(((rgb332 >> 5) & 0x07) * 36);
                        outputFrame[outputOffset + 3] = 255;
                        break;
                    case StreamColorMode.Indexed4:
                        UInt32 indexed4Color = Indexed4Palette[Math.Min(encodedTile[inputOffset++], (Byte)(Indexed4Palette.Length - 1))];
                        outputFrame[outputOffset] = (Byte)(indexed4Color & 0xFF);
                        outputFrame[outputOffset + 1] = (Byte)((indexed4Color >> 8) & 0xFF);
                        outputFrame[outputOffset + 2] = (Byte)((indexed4Color >> 16) & 0xFF);
                        outputFrame[outputOffset + 3] = (Byte)((indexed4Color >> 24) & 0xFF);
                        break;
                }

                outputOffset += 4;
            }
        }
    }

    private static Int32 GetBytesPerPixel(StreamColorMode colorMode)
    {
        return colorMode switch
        {
            StreamColorMode.Bgra32 => 4,
            StreamColorMode.Bgr24 => 3,
            StreamColorMode.Rgb565 => 2,
            StreamColorMode.Rgb332 => 1,
            StreamColorMode.Indexed4 => 1,
            _ => 2
        };
    }

    private static Boolean TryCreateTopologyPatchCandidate(Byte[] patchBytes, Int32 patchWidth, Int32 patchHeight, Int32 bytesPerPixel, Int32 patchDictionaryId, TileFrameEncodeState state, out PatchEncodingCandidate candidate)
    {
        if (!TryBuildTopologyPattern(
                patchBytes,
                patchWidth,
                patchHeight,
                bytesPerPixel,
                GetMaxTopologyPaletteColors(state.StaticCodebookBudgetBytes),
                out Byte[] topologyBytes,
                out UInt32[] paletteValues))
        {
            candidate = default;
            return false;
        }

        TileDictionaryKey topologyKey = TileDictionaryKey.FromBytes(topologyBytes);
        if (state.TopologyDictionary.TryGetId(topologyKey, out Int32 topologyDictionaryId, touch: false))
        {
            candidate = new PatchEncodingCandidate(
                PatchFrameOperationKind.TopologyReference,
                BuildTopologyReferencePayload(patchDictionaryId, topologyDictionaryId, paletteValues, bytesPerPixel),
                null,
                topologyDictionaryId);
            return true;
        }

        topologyDictionaryId = state.TopologyDictionary.ReserveId(topologyKey);
        candidate = new PatchEncodingCandidate(
            PatchFrameOperationKind.TopologyDefinition,
            BuildTopologyDefinitionPayload(patchDictionaryId, topologyDictionaryId, topologyBytes, paletteValues, bytesPerPixel),
            new TileDictionaryEntryPayload(topologyKey, topologyDictionaryId, topologyBytes),
            0);
        return true;
    }

    private static Boolean TryBuildTopologyPattern(Byte[] patchBytes, Int32 patchWidth, Int32 patchHeight, Int32 bytesPerPixel, Int32 maxPaletteColors, out Byte[] topologyBytes, out UInt32[] paletteValues)
    {
        if (patchWidth <= 0 || patchHeight <= 0 || patchBytes.Length == 0 || patchBytes.Length % bytesPerPixel != 0)
        {
            topologyBytes = Array.Empty<Byte>();
            paletteValues = Array.Empty<UInt32>();
            return false;
        }

        Int32 pixelCount = patchWidth * patchHeight;
        Dictionary<UInt32, Byte> paletteLookup = new Dictionary<UInt32, Byte>();
        List<UInt32> palette = new List<UInt32>(maxPaletteColors);
        Byte[] indices = new Byte[pixelCount];

        for (Int32 pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
        {
            UInt32 pixelValue = ReadPixelValue(patchBytes, pixelIndex * bytesPerPixel, bytesPerPixel);
            if (!paletteLookup.TryGetValue(pixelValue, out Byte paletteIndex))
            {
                if (palette.Count >= maxPaletteColors)
                {
                    topologyBytes = Array.Empty<Byte>();
                    paletteValues = Array.Empty<UInt32>();
                    return false;
                }

                paletteIndex = (Byte)palette.Count;
                paletteLookup.Add(pixelValue, paletteIndex);
                palette.Add(pixelValue);
            }

            indices[pixelIndex] = paletteIndex;
        }

        if (palette.Count <= 1)
        {
            topologyBytes = Array.Empty<Byte>();
            paletteValues = Array.Empty<UInt32>();
            return false;
        }

        Int32 bitsPerIndex = palette.Count <= 2 ? 1 : palette.Count <= 4 ? 2 : 4;
        Byte[] packedIndices = PackPaletteIndices(indices, bitsPerIndex);
        topologyBytes = BuildTopologyBytes(patchWidth, patchHeight, palette.Count, bitsPerIndex, packedIndices, indices);
        paletteValues = palette.ToArray();
        return true;
    }

    private static Byte[] BuildTopologyBytes(Int32 patchWidth, Int32 patchHeight, Int32 paletteCount, Int32 bitsPerIndex, Byte[] packedIndices, Byte[] unpackedIndices)
    {
        Byte[] topologyBytes = new Byte[4 + packedIndices.Length + unpackedIndices.Length];
        topologyBytes[0] = (Byte)patchWidth;
        topologyBytes[1] = (Byte)patchHeight;
        topologyBytes[2] = (Byte)paletteCount;
        topologyBytes[3] = (Byte)bitsPerIndex;
        Buffer.BlockCopy(packedIndices, 0, topologyBytes, 4, packedIndices.Length);
        Buffer.BlockCopy(unpackedIndices, 0, topologyBytes, 4 + packedIndices.Length, unpackedIndices.Length);
        return topologyBytes;
    }

    private static Byte[] BuildTopologyReferencePayload(Int32 patchDictionaryId, Int32 topologyDictionaryId, UInt32[] paletteValues, Int32 bytesPerPixel)
    {
        Int32 paletteByteCount = paletteValues.Length * bytesPerPixel;
        Byte[] payload = new Byte[4 + 4 + 1 + paletteByteCount];
        WriteDictionaryId(payload, patchDictionaryId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), topologyDictionaryId);
        payload[8] = (Byte)paletteValues.Length;
        Int32 offset = 9;
        foreach (UInt32 paletteValue in paletteValues)
        {
            WritePixelValue(payload, ref offset, paletteValue, bytesPerPixel);
        }

        return payload;
    }

    private static Byte[] BuildTopologyDefinitionPayload(Int32 patchDictionaryId, Int32 topologyDictionaryId, Byte[] topologyBytes, UInt32[] paletteValues, Int32 bytesPerPixel)
    {
        ParseTopologyBytes(topologyBytes, out _, out _, out Int32 paletteCount, out Int32 bitsPerIndex, out Byte[] packedIndices, out _);
        Int32 paletteByteCount = paletteValues.Length * bytesPerPixel;
        Byte[] payload = new Byte[4 + 4 + 1 + 1 + packedIndices.Length + paletteByteCount];
        WriteDictionaryId(payload, patchDictionaryId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), topologyDictionaryId);
        payload[8] = (Byte)paletteCount;
        payload[9] = (Byte)bitsPerIndex;
        packedIndices.AsSpan().CopyTo(payload.AsSpan(10, packedIndices.Length));
        Int32 offset = 10 + packedIndices.Length;
        foreach (UInt32 paletteValue in paletteValues)
        {
            WritePixelValue(payload, ref offset, paletteValue, bytesPerPixel);
        }

        return payload;
    }

    private static Byte[] DecodeTopologyDefinitionPatch(BinaryReader reader, Int32 patchWidth, Int32 patchHeight, Int32 bytesPerPixel, out Byte[] topologyBytes)
    {
        Int32 paletteCount = reader.ReadByte();
        Int32 bitsPerIndex = reader.ReadByte();
        if (paletteCount <= 1 || paletteCount > MaxPaletteColors || (bitsPerIndex != 1 && bitsPerIndex != 2 && bitsPerIndex != 4))
        {
            throw new InvalidDataException();
        }

        Int32 packedLength = ((patchWidth * patchHeight) * bitsPerIndex + 7) / 8;
        Byte[] packedIndices = reader.ReadBytes(packedLength);
        if (packedIndices.Length != packedLength)
        {
            throw new InvalidDataException();
        }

        Byte[] unpackedIndices = UnpackPaletteIndices(packedIndices, patchWidth * patchHeight, bitsPerIndex);
        topologyBytes = BuildTopologyBytes(patchWidth, patchHeight, paletteCount, bitsPerIndex, packedIndices, unpackedIndices);
        UInt32[] paletteValues = ReadTopologyPalette(reader, paletteCount, bytesPerPixel);
        return BuildPatchBytesFromTopology(topologyBytes, paletteValues, patchWidth, patchHeight, bytesPerPixel);
    }

    private static Byte[] DecodeTopologyReferencePatch(BinaryReader reader, Byte[] topologyBytes, Int32 patchWidth, Int32 patchHeight, Int32 bytesPerPixel)
    {
        ParseTopologyBytes(topologyBytes, out Int32 storedWidth, out Int32 storedHeight, out Int32 paletteCount, out _, out _, out _);
        if (storedWidth != patchWidth || storedHeight != patchHeight)
        {
            throw new InvalidDataException();
        }

        Int32 referencedPaletteCount = reader.ReadByte();
        if (referencedPaletteCount != paletteCount)
        {
            throw new InvalidDataException();
        }

        UInt32[] paletteValues = ReadTopologyPalette(reader, paletteCount, bytesPerPixel);
        return BuildPatchBytesFromTopology(topologyBytes, paletteValues, patchWidth, patchHeight, bytesPerPixel);
    }

    private static UInt32[] ReadTopologyPalette(BinaryReader reader, Int32 paletteCount, Int32 bytesPerPixel)
    {
        UInt32[] paletteValues = new UInt32[paletteCount];
        for (Int32 paletteIndex = 0; paletteIndex < paletteCount; paletteIndex++)
        {
            Byte[] paletteBytes = reader.ReadBytes(bytesPerPixel);
            if (paletteBytes.Length != bytesPerPixel)
            {
                throw new InvalidDataException();
            }

            paletteValues[paletteIndex] = ReadPixelValue(paletteBytes, 0, bytesPerPixel);
        }

        return paletteValues;
    }

    private static Byte[] BuildPatchBytesFromTopology(Byte[] topologyBytes, UInt32[] paletteValues, Int32 patchWidth, Int32 patchHeight, Int32 bytesPerPixel)
    {
        ParseTopologyBytes(topologyBytes, out Int32 storedWidth, out Int32 storedHeight, out Int32 paletteCount, out _, out _, out Byte[] unpackedIndices);
        if (storedWidth != patchWidth || storedHeight != patchHeight || paletteCount != paletteValues.Length)
        {
            throw new InvalidDataException();
        }

        Byte[] patchBytes = new Byte[patchWidth * patchHeight * bytesPerPixel];
        Int32 offset = 0;
        foreach (Byte paletteIndex in unpackedIndices)
        {
            if (paletteIndex >= paletteValues.Length)
            {
                throw new InvalidDataException();
            }

            WritePixelValue(patchBytes, ref offset, paletteValues[paletteIndex], bytesPerPixel);
        }

        return patchBytes;
    }

    private static void ParseTopologyBytes(Byte[] topologyBytes, out Int32 patchWidth, out Int32 patchHeight, out Int32 paletteCount, out Int32 bitsPerIndex, out Byte[] packedIndices, out Byte[] unpackedIndices)
    {
        if (topologyBytes.Length < 4)
        {
            throw new InvalidDataException();
        }

        patchWidth = topologyBytes[0];
        patchHeight = topologyBytes[1];
        paletteCount = topologyBytes[2];
        bitsPerIndex = topologyBytes[3];
        if (patchWidth <= 0 || patchHeight <= 0 || paletteCount <= 1 || paletteCount > MaxPaletteColors || (bitsPerIndex != 1 && bitsPerIndex != 2 && bitsPerIndex != 4))
        {
            throw new InvalidDataException();
        }

        Int32 packedLength = ((patchWidth * patchHeight) * bitsPerIndex + 7) / 8;
        Int32 unpackedLength = patchWidth * patchHeight;
        if (topologyBytes.Length != 4 + packedLength + unpackedLength)
        {
            throw new InvalidDataException();
        }

        packedIndices = new Byte[packedLength];
        Buffer.BlockCopy(topologyBytes, 4, packedIndices, 0, packedLength);
        unpackedIndices = new Byte[unpackedLength];
        Buffer.BlockCopy(topologyBytes, 4 + packedLength, unpackedIndices, 0, unpackedLength);
    }

    private static Byte[] PackIndexed4BytesIfNeeded(Byte[] encodedTile, StreamColorMode colorMode)
    {
        if (colorMode != StreamColorMode.Indexed4)
        {
            return encodedTile;
        }

        return PackPaletteIndices(encodedTile, 4);
    }

    private static Byte[] UnpackIndexed4BytesIfNeeded(Byte[] wireEncodedTile, StreamColorMode colorMode, Int32 tileWidth, Int32 tileHeight)
    {
        if (colorMode != StreamColorMode.Indexed4)
        {
            return wireEncodedTile;
        }

        return UnpackPaletteIndices(wireEncodedTile, tileWidth * tileHeight, 4);
    }

    private static Byte FindNearestIndexed4ColorIndex(Byte blue, Byte green, Byte red, Byte alpha)
    {
        Int32 bestIndex = 0;
        Int64 bestDistance = Int64.MaxValue;

        for (Int32 paletteIndex = 0; paletteIndex < Indexed4Palette.Length; paletteIndex++)
        {
            UInt32 paletteValue = Indexed4Palette[paletteIndex];
            Int32 blueDistance = blue - (Byte)(paletteValue & 0xFF);
            Int32 greenDistance = green - (Byte)((paletteValue >> 8) & 0xFF);
            Int32 redDistance = red - (Byte)((paletteValue >> 16) & 0xFF);
            Int32 alphaDistance = alpha - (Byte)((paletteValue >> 24) & 0xFF);
            Int64 distance = ((Int64)blueDistance * blueDistance) +
                             ((Int64)greenDistance * greenDistance) +
                             ((Int64)redDistance * redDistance) +
                             ((Int64)alphaDistance * alphaDistance);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            bestIndex = paletteIndex;
        }

        return (Byte)bestIndex;
    }

    private static Int32 GetMaxTopologyPaletteColors(Int64 staticCodebookBudgetBytes)
    {
        if (staticCodebookBudgetBytes >= 512L * 1024L * 1024L)
        {
            return 16;
        }

        if (staticCodebookBudgetBytes >= 128L * 1024L * 1024L)
        {
            return 8;
        }

        return 4;
    }
}
