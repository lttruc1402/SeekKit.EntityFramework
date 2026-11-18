namespace SeekKit.Core;

/// <summary>
/// Provider- and type-agnostic look-ahead keyset pagination engine, shared by the standard
/// (T-to-T) builders and the projected (T-to-TResult) builders. Owns everything about the
/// token — decoding, validating (sort fingerprint, expiry), and issuing next/previous
/// tokens — plus the look-ahead fetch, the optional total count, and walking every page.
/// </summary>
/// <typeparam name="T">The type the sort columns are defined on.</typeparam>
/// <typeparam name="TItem">The type of the returned items (<typeparamref name="T"/>, or a projection of it).</typeparam>
internal sealed class SeekPager<T, TItem>
{
    private readonly IReadOnlyList<ISortColumn<T>> _columns;
    private readonly ISeekSerializer _serializer;
    private readonly ISeekValueConverter _valueConverter;
    private readonly SeekOptionsBase _options;
    private readonly Func<SeekData?, SeekDirection, int, CancellationToken, Task<List<TItem>>> _fetch;
    private readonly Func<TItem, int, object?> _readSortValue;
    private readonly Func<CancellationToken, Task<long>>? _count;
    private readonly string _sortSignature;

    /// <param name="columns">The sort columns, in priority order.</param>
    /// <param name="serializer">Token serializer.</param>
    /// <param name="valueConverter">Keyset value converter.</param>
    /// <param name="options">Paging options.</param>
    /// <param name="fetch">Runs the ordered, keyset-filtered, limited query.</param>
    /// <param name="readSortValue">Reads the value of sort column <c>i</c> from an item.</param>
    /// <param name="count">Counts the whole unpaged set, or <see langword="null"/> when no total was requested.</param>
    public SeekPager(
        IReadOnlyList<ISortColumn<T>> columns,
        ISeekSerializer serializer,
        ISeekValueConverter valueConverter,
        SeekOptionsBase options,
        Func<SeekData?, SeekDirection, int, CancellationToken, Task<List<TItem>>> fetch,
        Func<TItem, int, object?> readSortValue,
        Func<CancellationToken, Task<long>>? count)
    {
        if (columns is null || columns.Count == 0)
            throw new InvalidOperationException("At least one order field is required. Use OrderBy() or OrderByDescending().");

        _columns        = columns;
        _serializer     = serializer;
        _valueConverter = valueConverter;
        _options        = options;
        _fetch          = fetch;
        _readSortValue  = readSortValue;
        _count          = count;
        _sortSignature  = SeekSortSignature.Compute(columns);
    }

    /// <summary>Fetches the single page <paramref name="request"/> points at.</summary>
    public ValueTask<SeekResult<TItem>> PageAsync(SeekRequest? request, CancellationToken cancellationToken)
        => PageAsync(request ?? new SeekRequest { PageSize = _options.DefaultPageSize }, _count, cancellationToken);

    /// <summary>
    /// Fetches the page <paramref name="request"/> points at and then keeps following
    /// <see cref="SeekResult{T}.NextToken"/> until the last page. The total (when requested)
    /// is counted once, for the first page, and carried over to the following ones.
    /// </summary>
    public async IAsyncEnumerable<SeekResult<TItem>> PagesAsync(
        SeekRequest? request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var current = request ?? new SeekRequest { PageSize = _options.DefaultPageSize };
        long? total = null;
        bool first = true;

        while (true)
        {
            var page = await PageAsync(current, first ? _count : null, cancellationToken);
            if (first)
                total = page.TotalCount;
            else
                page.TotalCount = total;
            first = false;

            yield return page;

            if (!page.HasNext || page.NextToken is null)
                yield break;

            current = new SeekRequest { Token = page.NextToken, PageSize = current.PageSize };
        }
    }

    /// <summary>Like <see cref="PagesAsync"/>, flattened to the individual items.</summary>
    public async IAsyncEnumerable<TItem> ItemsAsync(
        SeekRequest? request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var page in PagesAsync(request, cancellationToken))
        {
            foreach (var item in page.Items)
                yield return item;
        }
    }

    private async ValueTask<SeekResult<TItem>> PageAsync(
        SeekRequest request,
        Func<CancellationToken, Task<long>>? count,
        CancellationToken cancellationToken)
    {
        // null → first page: no token, or a token rejected under the lenient policy.
        var seekData = Decode(request.Token);

        var seekDirection = seekData?.Direction ?? SeekDirection.Next;
        bool isFirstPage = seekData is null;

        var pageSize = request.PageSize.GetValueOrDefault(_options.DefaultPageSize);
        pageSize = Math.Clamp(pageSize, _options.MinPageSize, _options.MaxPageSize);

        // Counted before the page is fetched and awaited on its own: an EF Core DbContext
        // does not allow two queries to run concurrently.
        long? totalCount = count is null ? null : await count(cancellationToken);

        // Look-ahead: fetch one extra row to detect whether a further page exists.
        var items = await _fetch(seekData, seekDirection, pageSize + 1, cancellationToken);

        var hasMore = items.Count > pageSize;
        if (hasMore)
            items.RemoveAt(items.Count - 1);

        if (seekDirection == SeekDirection.Previous)
            items.Reverse();

        string? nextToken = null;
        string? previousToken = null;

        if (items.Count > 0)
        {
            if (seekDirection == SeekDirection.Next)
            {
                previousToken = isFirstPage
                    ? null
                    : CreateCursor(items[0], SeekDirection.Previous);

                nextToken = hasMore
                    ? CreateCursor(items[^1], SeekDirection.Next)
                    : null;
            }
            else
            {
                previousToken = hasMore
                    ? CreateCursor(items[0], SeekDirection.Previous)
                    : null;

                nextToken = CreateCursor(items[^1], SeekDirection.Next);
            }
        }

        var hasNext = seekDirection == SeekDirection.Next
            ? hasMore
            : !isFirstPage;

        var hasPrevious = seekDirection == SeekDirection.Next
            ? !isFirstPage
            : hasMore;

        return new SeekResult<TItem>
        {
            Items = items.AsReadOnly(),
            NextToken = nextToken,
            PreviousToken = previousToken,
            HasNext = hasNext,
            HasPrevious = hasPrevious,
            Count = items.Count,
            TotalCount = totalCount,
            PageMetadata = new PageMetadata { PageSize = pageSize, RequestedAt = DateTime.UtcNow },
        };
    }

    /// <summary>
    /// Decodes and validates a client token. Returns <see langword="null"/> when there is no
    /// token, or when the token is unusable and the options say to fall back to the first page.
    /// </summary>
    private SeekData? Decode(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        // Serializers never throw: an undecodable or tampered token comes back empty.
        var seekData = _serializer.Deserialize(token!);
        if (seekData?.Values is not { Count: > 0 })
            return Reject(SeekTokenError.Malformed,
                "The pagination token could not be decoded: it is malformed, was not issued by this API, or its signature is invalid.");

        if (_options.TokenLifetime is not null)
        {
            if (seekData.ExpiresAt is null)
                return Reject(SeekTokenError.Expired,
                    "The pagination token carries no expiry and can no longer be used. Request the first page again.");

            if (_options.UtcNow().ToUnixTimeSeconds() > seekData.ExpiresAt.Value)
                return Reject(SeekTokenError.Expired,
                    "The pagination token has expired. Request the first page again.");
        }

        // Tokens issued before the fingerprint existed carry none and are let through.
        if (seekData.Sort is not null && !string.Equals(seekData.Sort, _sortSignature, StringComparison.Ordinal))
            throw new SeekTokenException(SeekTokenError.SortMismatch,
                "The pagination token was issued for a different sort order than this request uses. " +
                "A token is only valid together with the sort it was created with — request the first page again after changing the sort.");

        seekData.NullOrdering = _options.ResolveNullOrdering();
        return seekData;
    }

    private SeekData? Reject(SeekTokenError error, string message)
    {
        if (_options.InvalidTokenBehavior == SeekInvalidTokenBehavior.Throw)
            throw new SeekTokenException(error, message);

        return null;
    }

    private string CreateCursor(TItem item, SeekDirection direction)
    {
        var values = new Dictionary<string, string>(_columns.Count);

        for (int i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            // null (a NULL sort value) is stored as-is and serialized as JSON null.
            values[column.PropertyPath] = _valueConverter.ToString(_readSortValue(item, i), column.KeyType)!;
        }

        long? expiresAt = _options.TokenLifetime is { } lifetime
            ? _options.UtcNow().ToUnixTimeSeconds() + (long)Math.Ceiling(lifetime.TotalSeconds)
            : null;

        return _serializer.Serialize(new SeekData
        {
            Values = values,
            Direction = direction,
            Sort = _sortSignature,
            ExpiresAt = expiresAt,
        });
    }
}

/// <summary>
/// Computes the short fingerprint of a sort — which columns, in which order, in which
/// directions — that is embedded in every token so a token can't be replayed against a
/// query sorted differently.
/// </summary>
internal static class SeekSortSignature
{
    public static string Compute<T>(IReadOnlyList<ISortColumn<T>> columns)
    {
        // FNV-1a, 32 bit: stable across processes and runtimes (unlike string.GetHashCode),
        // and 8 hex characters is all the room worth spending inside a token.
        uint hash = 2166136261;

        for (int i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            foreach (char c in column.PropertyPath)
                hash = (hash ^ c) * 16777619;

            hash = (hash ^ (column.IsDescending ? 'd' : 'a')) * 16777619;
            hash = (hash ^ 0x1f) * 16777619;
        }

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }
}
