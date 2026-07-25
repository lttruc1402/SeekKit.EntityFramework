namespace SeekKit.Core;

/// <summary>
/// Provider- and type-agnostic look-ahead keyset pagination algorithm. Extracted from
/// <see cref="SeekBuilderCore{T}"/> so both the standard (T-to-T) pagination path and
/// the projected (T-to-TResult) path in <c>SeekProjectionBuilderBase&lt;T, TResult&gt;</c>
/// share one implementation of cursor decoding, look-ahead fetch, and token creation.
/// </summary>
internal static class SeekPagingAlgorithm
{
    public static async ValueTask<SeekResult<TItem>> ExecuteAsync<TItem>(
        SeekRequest request,
        SeekOptionsBase options,
        ISeekSerializer serializer,
        Func<SeekData?, SeekDirection, int, CancellationToken, Task<List<TItem>>> fetch,
        Func<TItem, SeekDirection, string> createCursor,
        CancellationToken cancellationToken)
    {
        SeekDirection seekDirection = SeekDirection.Next;
        SeekData? seekData = null;

        bool isNullToken = string.IsNullOrWhiteSpace(request.Token);

        if (!isNullToken)
        {
            seekData = serializer.Deserialize(request.Token!);
            seekDirection = seekData.Direction;
        }

        var pageSize = request.PageSize.GetValueOrDefault(options.DefaultPageSize);
        pageSize = Math.Clamp(pageSize, options.MinPageSize, options.MaxPageSize);

        // A cursor with no values (malformed/first-page fallback) applies no filter.
        var effectiveSeekData = seekData is { Values.Count: > 0 } ? seekData : null;

        // Look-ahead: fetch one extra row to detect whether a further page exists.
        var items = await fetch(effectiveSeekData, seekDirection, pageSize + 1, cancellationToken);

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
                previousToken = isNullToken
                    ? null
                    : createCursor(items[0], SeekDirection.Previous);

                nextToken = hasMore
                    ? createCursor(items[^1], SeekDirection.Next)
                    : null;
            }
            else
            {
                previousToken = hasMore
                    ? createCursor(items[0], SeekDirection.Previous)
                    : null;

                nextToken = createCursor(items[^1], SeekDirection.Next);
            }
        }

        var hasNext = seekDirection == SeekDirection.Next
            ? hasMore
            : !isNullToken;

        var hasPrevious = seekDirection == SeekDirection.Next
            ? !isNullToken
            : hasMore;

        return new SeekResult<TItem>
        {
            Items = items.AsReadOnly(),
            NextToken = nextToken,
            PreviousToken = previousToken,
            HasNext = hasNext,
            HasPrevious = hasPrevious,
            Count = items.Count,
            PageMetadata = new PageMetadata { PageSize = pageSize, RequestedAt = DateTime.UtcNow },
        };
    }
}
