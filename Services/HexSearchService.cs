using System.Globalization;
using System.Text;

namespace NexusProgrammer;

public static class HexSearchService
{
    public static bool TryParseHexPattern(string text, out byte[] pattern)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (Uri.IsHexDigit(ch))
            {
                if (ch == '0' && i + 1 < text.Length && text[i + 1] is 'x' or 'X')
                {
                    i++;
                    continue;
                }

                builder.Append(ch);
                continue;
            }

            if (char.IsWhiteSpace(ch) || ch is '-' or '_' or ',' or ';')
            {
                continue;
            }

            pattern = [];
            return false;
        }

        var hex = builder.ToString();
        if (hex.Length == 0 || hex.Length % 2 != 0)
        {
            pattern = [];
            return false;
        }

        pattern = new byte[hex.Length / 2];
        for (var i = 0; i < pattern.Length; i++)
        {
            if (!byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, null, out pattern[i]))
            {
                pattern = [];
                return false;
            }
        }

        return true;
    }

    public static string FormatHexPattern(byte[] pattern) =>
        string.Join(" ", pattern.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    public static bool TryParseOffset(string text, out int offset)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return int.TryParse(text, NumberStyles.HexNumber, null, out offset);
    }

    public static int FindBytes(byte[] buffer, byte[] pattern, int startOffset, bool forward)
    {
        if (pattern.Length == 0 || pattern.Length > buffer.Length)
        {
            return -1;
        }

        startOffset = Math.Clamp(startOffset, 0, buffer.Length - 1);
        if (forward)
        {
            var index = buffer.AsSpan(startOffset).IndexOf(pattern);
            return index < 0 ? -1 : startOffset + index;
        }

        for (var offset = Math.Min(startOffset, buffer.Length - pattern.Length); offset >= 0; offset--)
        {
            if (buffer.AsSpan(offset, pattern.Length).SequenceEqual(pattern))
            {
                return offset;
            }
        }

        return -1;
    }

    public static List<int> FindAllBytes(byte[] buffer, byte[] pattern)
    {
        var offsets = new List<int>();
        if (pattern.Length == 0 || pattern.Length > buffer.Length)
        {
            return offsets;
        }

        var offset = 0;
        while (offset <= buffer.Length - pattern.Length)
        {
            var index = buffer.AsSpan(offset).IndexOf(pattern);
            if (index < 0)
            {
                break;
            }

            var absolute = offset + index;
            offsets.Add(absolute);
            offset = absolute + 1;
        }

        return offsets;
    }

    public static int FindAsciiText(byte[] buffer, byte[] pattern, int startOffset, bool forward)
    {
        if (pattern.Length == 0 || pattern.Length > buffer.Length)
        {
            return -1;
        }

        startOffset = Math.Clamp(startOffset, 0, buffer.Length - 1);
        if (forward)
        {
            for (var offset = startOffset; offset <= buffer.Length - pattern.Length; offset++)
            {
                if (AsciiEqualsIgnoreCase(buffer, pattern, offset))
                {
                    return offset;
                }
            }

            return -1;
        }

        for (var offset = Math.Min(startOffset, buffer.Length - pattern.Length); offset >= 0; offset--)
        {
            if (AsciiEqualsIgnoreCase(buffer, pattern, offset))
            {
                return offset;
            }
        }

        return -1;
    }

    public static List<int> FindAllAsciiText(byte[] buffer, byte[] pattern)
    {
        var offsets = new List<int>();
        if (pattern.Length == 0 || pattern.Length > buffer.Length)
        {
            return offsets;
        }

        for (var offset = 0; offset <= buffer.Length - pattern.Length; offset++)
        {
            if (AsciiEqualsIgnoreCase(buffer, pattern, offset))
            {
                offsets.Add(offset);
            }
        }

        return offsets;
    }

    public static bool AsciiEqualsIgnoreCase(byte[] buffer, byte[] pattern, int offset)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            if (ToAsciiUpper(buffer[offset + i]) != ToAsciiUpper(pattern[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static byte ToAsciiUpper(byte value) => value is >= (byte)'a' and <= (byte)'z'
        ? (byte)(value - 32)
        : value;
}
