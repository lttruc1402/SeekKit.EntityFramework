namespace SeekKit.EntityFramework.Core;

/// <summary>
/// Serializes and deserializes <see cref="SeekData"/> to and from an opaque,
/// URL-safe Base64 cursor token. The default implementation uses JSON + Base64url
/// (no padding, <c>+</c>/<c>/</c> replaced with <c>-</c>/<c>_</c>).
/// </summary>

public interface ISeekSerializer
{
    /// <summary>
    /// Encodes <paramref name="seekData"/> into a URL-safe Base64 cursor token
    /// suitable for inclusion in query strings or JSON responses.
    /// </summary>
    /// <param name="seekData">The keyset boundary values and direction to encode.</param>
    /// <returns>A non-padded, URL-safe Base64 string.</returns>
    string Serialize(SeekData seekData);

    /// <summary>
    /// Decodes a cursor token back to a <see cref="SeekData"/> instance.
    /// Returns an empty <see cref="SeekData"/> when the token is <c>null</c>,
    /// empty, or malformed — it never throws.
    /// </summary>
    /// <param name="cursor">The opaque token previously produced by <see cref="Serialize"/>.</param>
    SeekData Deserialize(string cursor);
}

internal sealed class SeekSerializer: ISeekSerializer
{
    public string Serialize(SeekData seekData)
    {
        if (seekData is null)
            return string.Empty;

#if NET6_0_OR_GREATER
        var bytes = JsonSerializer.SerializeToUtf8Bytes(seekData, SeekDataJsonSerializer.Default.SeekData);
#else
        var bytes = JsonSerializer.SerializeToUtf8Bytes(seekData);
#endif
        return Base64Helper.Base64UrlEncode(bytes);
    }

    public SeekData Deserialize(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return new();

        try
        {
            if (Base64Helper.Base64UrlDecode(cursor, out Span<byte> buffer))
            {
#if NET6_0_OR_GREATER
                return JsonSerializer.Deserialize(buffer, SeekDataJsonSerializer.Default.SeekData)
                    ?? new();
#else
                return JsonSerializer.Deserialize<SeekData>(buffer) ?? new();
#endif
            }

            return new();
        }
        catch
        {
            // Malformed or tampered token — the contract is to never throw
            // and fall back to the first page.
            return new();
        }
    }
}
