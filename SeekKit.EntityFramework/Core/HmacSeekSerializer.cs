namespace SeekKit.EntityFramework.Core;

/// <summary>
/// An <see cref="ISeekSerializer"/> that signs tokens with HMAC-SHA256 so they
/// cannot be tampered with or forged by clients. Token format:
/// <c>base64url(json-payload).base64url(hmac-sha256)</c>.
/// Register via <see cref="SeekKitConfiguration.UseHmacSigning(byte[])"/>.
/// </summary>
internal sealed class HmacSeekSerializer : ISeekSerializer
{
    private const char Separator = '.';
    private const int MinKeyLength = 16;

    private readonly byte[] _key;

    public HmacSeekSerializer(byte[] key)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));
        if (key.Length < MinKeyLength)
            throw new ArgumentException(
                $"HMAC key must be at least {MinKeyLength} bytes ({key.Length} given). " +
                "Use a long random secret, e.g. 32 bytes from a secure generator.",
                nameof(key));

        _key = (byte[])key.Clone();
    }

    public string Serialize(SeekData seekData)
    {
        if (seekData is null)
            return string.Empty;

#if NET6_0_OR_GREATER
        var payload = JsonSerializer.SerializeToUtf8Bytes(seekData, SeekDataJsonSerializer.Default.SeekData);
#else
        var payload = JsonSerializer.SerializeToUtf8Bytes(seekData);
#endif
        var signature = ComputeHmac(payload);

        return string.Concat(
            Base64Helper.Base64UrlEncode(payload),
            Separator.ToString(),
            Base64Helper.Base64UrlEncode(signature));
    }

    public SeekData Deserialize(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return new();

        try
        {
            int sep = cursor.LastIndexOf(Separator);
            if (sep <= 0 || sep == cursor.Length - 1)
                return new();   // no signature part — reject

            if (!Base64Helper.Base64UrlDecode(cursor.Substring(0, sep), out Span<byte> payload))
                return new();
            if (!Base64Helper.Base64UrlDecode(cursor.Substring(sep + 1), out Span<byte> signature))
                return new();

            var expected = ComputeHmac(payload);
            if (!FixedTimeEquals(signature, expected))
                return new();   // tampered or foreign token — fall back to first page

#if NET6_0_OR_GREATER
            return JsonSerializer.Deserialize(payload, SeekDataJsonSerializer.Default.SeekData) ?? new();
#else
            return JsonSerializer.Deserialize<SeekData>(payload) ?? new();
#endif
        }
        catch
        {
            // Contract: never throw — treat any malformed token as "first page".
            return new();
        }
    }

    private byte[] ComputeHmac(ReadOnlySpan<byte> payload)
    {
#if NET6_0_OR_GREATER
        return HMACSHA256.HashData(_key, payload);
#else
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(payload.ToArray());
#endif
    }

    private static bool FixedTimeEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
#if NET6_0_OR_GREATER
        return CryptographicOperations.FixedTimeEquals(left, right);
#else
        // Constant-time comparison to avoid leaking signature bytes via timing.
        if (left.Length != right.Length)
            return false;

        int diff = 0;
        for (int i = 0; i < left.Length; i++)
            diff |= left[i] ^ right[i];

        return diff == 0;
#endif
    }
}
