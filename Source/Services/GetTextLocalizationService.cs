using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ShadowLink.Localization;

namespace ShadowLink.Services;

public sealed class GetTextLocalizationService : IShadowLinkLocalizationService
{
    private const UInt32 LittleEndianMagic = 0x950412de;
    private const UInt32 BigEndianMagic = 0xde120495;
    private const String Domain = "ui";
    private const String FallbackLocale = "en_US";
    private Dictionary<String, String>? _catalog;

    public String GetText(String key)
    {
        if (String.IsNullOrWhiteSpace(key))
        {
            return String.Empty;
        }

        Dictionary<String, String> catalog = _catalog ??= LoadCatalog();
        String value = catalog.TryGetValue(key, out String? translatedValue)
            ? translatedValue
            : String.Empty;
        return String.IsNullOrWhiteSpace(value) ? key : value;
    }

    private Dictionary<String, String> LoadCatalog()
    {
        String resourceName = ResolveResourceName();
        Assembly assembly = typeof(GetTextLocalizationService).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        return stream is null
            ? new Dictionary<String, String>(StringComparer.Ordinal)
            : ReadMoCatalog(stream);
    }

    private static String ResolveResourceName()
    {
        CultureInfo requestedCulture = CultureInfo.CurrentUICulture;
        String requestedFolder = requestedCulture.Name.Replace('-', '_');
        String requestedResourceName = BuildResourceName(requestedFolder);
        Assembly assembly = typeof(GetTextLocalizationService).Assembly;
        if (assembly.GetManifestResourceInfo(requestedResourceName) is not null)
        {
            return requestedResourceName;
        }

        return BuildResourceName(FallbackLocale);
    }

    private static String BuildResourceName(String localeFolder)
    {
        return String.Join(
            '.',
            typeof(GetTextLocalizationService).Assembly.GetName().Name,
            "Locales",
            localeFolder,
            "LC_MESSAGES",
            Domain,
            "mo");
    }

    private static Dictionary<String, String> ReadMoCatalog(Stream stream)
    {
        using BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        UInt32 magic = reader.ReadUInt32();
        Boolean isLittleEndian = magic switch
        {
            LittleEndianMagic => true,
            BigEndianMagic => false,
            _ => throw new InvalidDataException()
        };

        _ = ReadUInt32(reader, isLittleEndian);
        UInt32 entryCount = ReadUInt32(reader, isLittleEndian);
        UInt32 originalTableOffset = ReadUInt32(reader, isLittleEndian);
        UInt32 translationTableOffset = ReadUInt32(reader, isLittleEndian);
        _ = ReadUInt32(reader, isLittleEndian);
        _ = ReadUInt32(reader, isLittleEndian);

        List<(UInt32 Length, UInt32 Offset)> originalEntries = ReadStringTable(reader, entryCount, originalTableOffset, isLittleEndian);
        List<(UInt32 Length, UInt32 Offset)> translationEntries = ReadStringTable(reader, entryCount, translationTableOffset, isLittleEndian);

        Encoding encoding = Encoding.UTF8;
        Dictionary<String, String> catalog = new Dictionary<String, String>(StringComparer.Ordinal);

        for (Int32 index = 0; index < originalEntries.Count; index++)
        {
            String original = ReadMoString(reader, originalEntries[index], encoding);
            String translation = ReadMoString(reader, translationEntries[index], encoding);

            if (original.Length == 0)
            {
                encoding = ResolveEncodingFromHeader(translation);
                continue;
            }

            Int32 pluralSeparatorIndex = original.IndexOf('\0');
            if (pluralSeparatorIndex >= 0)
            {
                original = original[..pluralSeparatorIndex];
                Int32 translatedPluralSeparatorIndex = translation.IndexOf('\0');
                if (translatedPluralSeparatorIndex >= 0)
                {
                    translation = translation[..translatedPluralSeparatorIndex];
                }
            }

            catalog[original] = translation;
        }

        return catalog;
    }

    private static List<(UInt32 Length, UInt32 Offset)> ReadStringTable(BinaryReader reader, UInt32 entryCount, UInt32 tableOffset, Boolean isLittleEndian)
    {
        reader.BaseStream.Seek(tableOffset, SeekOrigin.Begin);
        List<(UInt32 Length, UInt32 Offset)> entries = new List<(UInt32 Length, UInt32 Offset)>((Int32)entryCount);
        for (UInt32 index = 0; index < entryCount; index++)
        {
            UInt32 length = ReadUInt32(reader, isLittleEndian);
            UInt32 offset = ReadUInt32(reader, isLittleEndian);
            entries.Add((length, offset));
        }

        return entries;
    }

    private static String ReadMoString(BinaryReader reader, (UInt32 Length, UInt32 Offset) entry, Encoding encoding)
    {
        if (entry.Length == 0)
        {
            return String.Empty;
        }

        reader.BaseStream.Seek(entry.Offset, SeekOrigin.Begin);
        Byte[] data = reader.ReadBytes((Int32)entry.Length);
        return encoding.GetString(data);
    }

    private static UInt32 ReadUInt32(BinaryReader reader, Boolean isLittleEndian)
    {
        Byte[] bytes = reader.ReadBytes(sizeof(UInt32));
        if (bytes.Length != sizeof(UInt32))
        {
            throw new EndOfStreamException();
        }

        if (BitConverter.IsLittleEndian != isLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToUInt32(bytes, 0);
    }

    private static Encoding ResolveEncodingFromHeader(String header)
    {
        String? charset = header
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase)
                ? line
                : null)
            .FirstOrDefault(line => line is not null);

        if (charset is null)
        {
            return Encoding.UTF8;
        }

        Int32 charsetIndex = charset.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (charsetIndex < 0)
        {
            return Encoding.UTF8;
        }

        String encodingName = charset[(charsetIndex + "charset=".Length)..].Trim();
        Int32 separatorIndex = encodingName.IndexOf(';');
        if (separatorIndex >= 0)
        {
            encodingName = encodingName[..separatorIndex].Trim();
        }

        try
        {
            return Encoding.GetEncoding(encodingName);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}
