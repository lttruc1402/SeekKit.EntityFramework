namespace SeekKit.Core.Helpers;

internal static class Base64Helper
{
    public const int MaxStackSize = 512;
    public const char PaddingChar = '=';

    /// <summary>
    /// Encodes bytes as URL-safe Base64: no padding, <c>+</c>/<c>/</c> replaced with <c>-</c>/<c>_</c>.
    /// </summary>
    internal static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        int base64Len = ((bytes.Length + 2) / 3) * 4;
        Span<char> buffer = base64Len <= MaxStackSize
            ? stackalloc char[base64Len]
            : new char[base64Len];

        Convert.TryToBase64Chars(bytes, buffer, out int written);

        int end = written;
        while (end > 0 && buffer[end - 1] == PaddingChar)
            end--;

        for (int i = 0; i < end; i++)
        {
            ref var c = ref buffer[i];
            if (c == '+')
            {
                c = '-';
                continue;
            }
            if (c == '/')
            {
                c = '_';
            }
        }

        return new string(buffer[..end]);
    }

    /// <summary>
    /// Decodes a URL-safe Base64 string. Returns <c>false</c> (with an empty
    /// <paramref name="output"/>) for malformed input instead of throwing.
    /// </summary>
    internal static bool Base64UrlDecode(string input, out Span<byte> output)
    {
        int padding = (4 - input.Length % 4) % 4;
        int totalLength = input.Length + padding;

        Span<char> buffer = totalLength <= MaxStackSize
            ? stackalloc char[totalLength]
            : new char[totalLength];

        input.AsSpan().CopyTo(buffer);

        for (int i = 0; i < input.Length; i++)
        {
            ref var c = ref buffer[i];
            if (c == '-')
            {
                c = '+';
                continue;
            }
            if (c == '_')
            {
                c = '/';
            }
        }

        for (int i = input.Length; i < totalLength; i++)
        {
            buffer[i] = PaddingChar;
        }

        var decoded = new byte[(totalLength * 3) / 4];
        if (Convert.TryFromBase64Chars(buffer, decoded, out int bytesWritten))
        {
            output = decoded.AsSpan(0, bytesWritten);
            return true;
        }

        output = default;
        return false;
    }
}
