using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NexusProgrammer;

public static class WindowsKeyFinder
{
    private const int KeyLength = 29;
    private const int LenovoLenvHeaderLength = 0x10;
    private const int LenovoLenvBlockLength = 0x1000;
    private const int LenovoLenvEntryHeaderLength = 0x18;
    private const int LenovoLenvMaxEntries = 256;

    private static readonly byte[] LenovoLenvMarker = Encoding.ASCII.GetBytes("LENV");
    private static readonly byte[] OemMarker =
    [
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x1D, 0x00, 0x00, 0x00
    ];

    private static readonly string[] Anchors =
    [
        "Windows",
        "Product",
        "ProductKey",
        "DigitalProductId"
    ];

    private static readonly Dictionary<string, string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["7H3HT-N36VD-XK866-8RV8Y-39M6M"] = "Win 10 RTM Core OEM:DM, EULA OEM",
        ["TX9XD-98N7V-6WMQ6-BX7FG-H8Q99"] = "Windows 10/11 Home generic install key, Retail channel",
        ["VK7JG-NPHTM-C97JM-9MPGT-3V66T"] = "Windows 10/11 Pro generic install key, Retail channel",
        ["W269N-WFGWX-YVC9B-4J6C9-T83GX"] = "Windows 10/11 Pro generic install key, Volume KMS client",
        ["NPPR9-FWDCX-D2C8J-H872K-2YT43"] = "Windows 10/11 Enterprise generic install key, Volume KMS client",
        ["MH37W-N47XK-V7XM9-C7227-GCQG9"] = "Windows 10/11 Pro N generic install key, Retail channel",
        ["NW6C2-QMPVW-D7KKK-3GKT6-VCFB2"] = "Windows 10/11 Education generic install key, Volume KMS client",
        ["2WH4N-8QGBV-H22JP-CT43Q-MDWWJ"] = "Windows 10/11 Education N generic install key, Volume KMS client"
    };

    public static List<WindowsKeyCandidate> Find(byte[] buffer)
    {
        var byOffset = new Dictionary<int, WindowsKeyCandidate>();
        if (buffer.Length < KeyLength)
        {
            return [];
        }

        AddBinaryMarkerMatches(buffer, byOffset);
        AddAsciiMarkerMatches(buffer, "MSDM", 512, "ACPI MSDM", byOffset);
        AddLenovoLenvDmiMatches(buffer, byOffset);
        foreach (var anchor in Anchors)
        {
            AddAsciiMarkerMatches(buffer, anchor, 768, $"Near {anchor}", byOffset);
        }

        AddRangeMatches(buffer, 0, buffer.Length, "Direct pattern", byOffset);

        return byOffset.Values
            .OrderBy(candidate => MethodPriority(candidate.Method))
            .ThenBy(candidate => candidate.Offset)
            .ToList();
    }

    private static void AddBinaryMarkerMatches(byte[] buffer, Dictionary<int, WindowsKeyCandidate> byOffset)
    {
        foreach (var markerOffset in FindAll(buffer, OemMarker, 0))
        {
            AddRangeMatches(buffer, markerOffset + OemMarker.Length, 256, "Hex marker", byOffset);
        }
    }

    private static void AddLenovoLenvDmiMatches(byte[] buffer, Dictionary<int, WindowsKeyCandidate> byOffset)
    {
        foreach (var blockOffset in FindAll(buffer, LenovoLenvMarker, 0))
        {
            if (blockOffset + LenovoLenvHeaderLength >= buffer.Length)
            {
                continue;
            }

            var blockLength = Math.Min(LenovoLenvBlockLength, buffer.Length - blockOffset);
            var bodyLength = blockLength - LenovoLenvHeaderLength;
            if (bodyLength < LenovoLenvEntryHeaderLength)
            {
                continue;
            }

            var decodedBody = new byte[bodyLength];
            var xorKey = buffer[blockOffset + 0x0D];
            for (var index = 0; index < decodedBody.Length; index++)
            {
                decodedBody[index] = (byte)(buffer[blockOffset + LenovoLenvHeaderLength + index] ^ xorKey);
            }

            if (!AddLenovoLenvEntryMatches(decodedBody, blockOffset + LenovoLenvHeaderLength, buffer, blockOffset, byOffset))
            {
                AddDecodedRangeMatches(decodedBody, 0, decodedBody.Length, blockOffset + LenovoLenvHeaderLength, "Lenovo LENV XOR DMI", byOffset);
            }
        }
    }

    private static bool AddLenovoLenvEntryMatches(byte[] decodedBody, int bodyBaseOffset, byte[] originalBuffer, int blockOffset, Dictionary<int, WindowsKeyCandidate> byOffset)
    {
        var entryCount = ReadUInt32LittleEndian(originalBuffer, blockOffset + 0x08);
        if (entryCount <= 0 || entryCount > LenovoLenvMaxEntries)
        {
            return false;
        }

        var foundEntry = false;
        var entryOffset = 0;
        for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
        {
            if (entryOffset + LenovoLenvEntryHeaderLength > decodedBody.Length)
            {
                break;
            }

            var dataSize = ReadUInt32LittleEndian(decodedBody, entryOffset + 0x10);
            if (dataSize < 0 || dataSize > decodedBody.Length - entryOffset - LenovoLenvEntryHeaderLength)
            {
                break;
            }

            foundEntry = true;
            AddDecodedRangeMatches(decodedBody, entryOffset + LenovoLenvEntryHeaderLength, dataSize, bodyBaseOffset, "Lenovo LENV XOR DMI", byOffset);
            entryOffset += LenovoLenvEntryHeaderLength + dataSize;
        }

        return foundEntry;
    }

    private static int ReadUInt32LittleEndian(byte[] buffer, int offset)
    {
        if (offset < 0 || offset + 4 > buffer.Length)
        {
            return -1;
        }

        var value = BitConverter.ToUInt32(buffer, offset);
        return value > int.MaxValue ? -1 : (int)value;
    }

    private static void AddAsciiMarkerMatches(byte[] buffer, string marker, int windowLength, string method, Dictionary<int, WindowsKeyCandidate> byOffset)
    {
        AddRangeMatchesForEach(buffer, Encoding.ASCII.GetBytes(marker), windowLength, method, byOffset);
    }

    private static void AddRangeMatchesForEach(byte[] buffer, byte[] marker, int windowLength, string method, Dictionary<int, WindowsKeyCandidate> byOffset)
    {
        foreach (var markerOffset in FindAll(buffer, marker, 0))
        {
            AddRangeMatches(buffer, markerOffset, windowLength, method, byOffset);
        }
    }

    private static void AddDecodedRangeMatches(byte[] buffer, int start, int length, int baseOffset, string method, Dictionary<int, WindowsKeyCandidate> byOffset)
    {
        if (start < 0 || start >= buffer.Length || length <= 0)
        {
            return;
        }

        var end = Math.Min(buffer.Length, start + length);
        for (var offset = start; offset <= end - KeyLength; offset++)
        {
            var key = TryReadKey(buffer, offset);
            if (key is null)
            {
                continue;
            }

            AddCandidate(byOffset, baseOffset + offset, method, key);
        }
    }

    private static void AddRangeMatches(byte[] buffer, int start, int length, string method, Dictionary<int, WindowsKeyCandidate> byOffset)
    {
        if (start < 0 || start >= buffer.Length || length <= 0)
        {
            return;
        }

        var end = Math.Min(buffer.Length, start + length);
        for (var offset = start; offset <= end - KeyLength; offset++)
        {
            var key = TryReadKey(buffer, offset);
            if (key is null)
            {
                continue;
            }

            AddCandidate(byOffset, offset, method, key);
        }
    }

    private static void AddCandidate(Dictionary<int, WindowsKeyCandidate> byOffset, int offset, string method, string key)
    {
        if (!byOffset.TryGetValue(offset, out var existing) || MethodPriority(method) < MethodPriority(existing.Method))
        {
            byOffset[offset] = new WindowsKeyCandidate(method, offset, key, KeyLength, Classify(key, method));
        }
    }

    private static string? TryReadKey(byte[] buffer, int offset)
    {
        var chars = new char[KeyLength];
        for (var index = 0; index < KeyLength; index++)
        {
            var value = buffer[offset + index];
            if (index is 5 or 11 or 17 or 23)
            {
                if (value != '-')
                {
                    return null;
                }

                chars[index] = '-';
                continue;
            }

            if (!IsAsciiLetterOrDigit(value))
            {
                return null;
            }

            chars[index] = char.ToUpperInvariant((char)value);
        }

        var key = new string(chars);
        return IsValidCandidate(key) ? key : null;
    }

    private static bool IsValidCandidate(string key)
    {
        var compact = key.Replace("-", string.Empty, StringComparison.Ordinal);
        if (!compact.Any(char.IsLetter) || !compact.Any(char.IsDigit))
        {
            return false;
        }

        var groups = key.Split('-');
        if (groups.All(group => string.Equals(group, groups[0], StringComparison.Ordinal)))
        {
            return false;
        }

        return compact.Distinct().Count() >= 6;
    }

    private static bool IsAsciiLetterOrDigit(byte value) =>
        value is >= (byte)'0' and <= (byte)'9' ||
        value is >= (byte)'A' and <= (byte)'Z' ||
        value is >= (byte)'a' and <= (byte)'z';

    private static IEnumerable<int> FindAll(byte[] buffer, byte[] pattern, int start)
    {
        if (pattern.Length == 0 || pattern.Length > buffer.Length)
        {
            yield break;
        }

        for (var offset = Math.Max(0, start); offset <= buffer.Length - pattern.Length; offset++)
        {
            var match = true;
            for (var index = 0; index < pattern.Length; index++)
            {
                if (buffer[offset + index] != pattern[index])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                yield return offset;
            }
        }
    }

    private static string Classify(string key, string method)
    {
        var pidGen = TryClassifyWithPidGenX(key);
        if (!string.IsNullOrWhiteSpace(pidGen))
        {
            return pidGen;
        }

        if (KnownKeys.TryGetValue(key, out var known))
        {
            return known;
        }

        if (method is "Hex marker" or "ACPI MSDM")
        {
            return "likely OEM:DM embedded key";
        }

        if (method == "Lenovo LENV XOR DMI")
        {
            return "likely Lenovo XOR-decoded DMI/OEM key";
        }

        if (method.Contains("DigitalProductId", StringComparison.OrdinalIgnoreCase))
        {
            return "likely installed Windows product key";
        }

        return method.StartsWith("Near ", StringComparison.Ordinal)
            ? "possible Windows product key"
            : "product key candidate";
    }

    private static string? TryClassifyWithPidGenX(string key)
    {
        try
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var pkeyConfigPath = Path.Combine(windows, "System32", "spp", "tokens", "pkeyconfig", "pkeyconfig.xrm-ms");
            if (!File.Exists(pkeyConfigPath))
            {
                return null;
            }

            var digitalProductId4 = new byte[0x04F8];
            digitalProductId4[0] = 0xF8;
            digitalProductId4[1] = 0x04;

            var result = PidGenX(key, pkeyConfigPath, "00000", 0, IntPtr.Zero, IntPtr.Zero, digitalProductId4);
            if (result != 0)
            {
                return null;
            }

            var strings = ReadPrintableUtf16Strings(digitalProductId4);
            var edition = strings.FirstOrDefault(IsEditionString);
            var eula = strings.FirstOrDefault(value =>
                string.Equals(value, "OEM", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Retail", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Volume", StringComparison.OrdinalIgnoreCase));
            var channel = ReadUtf16String(digitalProductId4, 1016, 128);

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(edition))
            {
                parts.Add(edition);
            }

            if (!string.IsNullOrWhiteSpace(eula))
            {
                parts.Add(eula);
            }

            if (!string.IsNullOrWhiteSpace(channel))
            {
                parts.Add($"EULA {channel}");
            }

            return parts.Count == 0 ? null : string.Join(", ", parts);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ReadPrintableUtf16Strings(byte[] buffer)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        for (var offset = 0; offset + 1 < buffer.Length; offset += 2)
        {
            var value = BitConverter.ToUInt16(buffer, offset);
            if (value is >= 0x20 and <= 0x7E)
            {
                current.Append((char)value);
                continue;
            }

            AddPrintableString(values, current);
        }

        AddPrintableString(values, current);
        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddPrintableString(List<string> values, StringBuilder current)
    {
        if (current.Length >= 3)
        {
            values.Add(current.ToString().Trim());
        }

        current.Clear();
    }

    private static string? ReadUtf16String(byte[] buffer, int offset, int length)
    {
        if (offset < 0 || length <= 0 || offset >= buffer.Length)
        {
            return null;
        }

        var count = Math.Min(length, buffer.Length - offset);
        var value = Encoding.Unicode.GetString(buffer, offset, count);
        var terminator = value.IndexOf('\0');
        if (terminator >= 0)
        {
            value = value[..terminator];
        }

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsEditionString(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Contains('-', StringComparison.Ordinal) ||
            value.Contains('.', StringComparison.Ordinal) ||
            Guid.TryParse(value, out _))
        {
            return false;
        }

        return value.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Professional", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Enterprise", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Education", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Server", StringComparison.OrdinalIgnoreCase);
    }

    private static int MethodPriority(string method)
    {
        return method switch
        {
            "Hex marker" => 0,
            "ACPI MSDM" => 1,
            "Lenovo LENV XOR DMI" => 2,
            _ when method.StartsWith("Near ", StringComparison.Ordinal) => 3,
            _ => 4
        };
    }

    [DllImport("pidgenx.dll", CharSet = CharSet.Unicode, EntryPoint = "PidGenX")]
    private static extern int PidGenX(
        string productKey,
        string pkeyConfigPath,
        string mpc,
        int unknownUsage,
        IntPtr activationId,
        IntPtr productId,
        [Out] byte[] digitalProductId4);
}
