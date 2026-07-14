namespace SeekKit.Core;

/// <summary>
/// Provider-agnostic core of a SeekKit pagination builder. Owns the keyset
/// algorithm that is identical across providers and query models: cursor
/// decoding, page-size clamping, look-ahead paging, backward reversal, and
/// next/previous token generation.
/// <para>
/// Concrete builders implement <see cref="FetchAsync"/> to run the actual
/// ordered + keyset-filtered + limited query on their query model
/// (<c>IQueryable&lt;T&gt;</c> for EF Core / MongoDB LINQ, or
/// <c>IAggregateFluent&lt;T&gt;</c> for the MongoDB aggregation pipeline).
/// </para>
/// </summary>
/// <typeparam name="T">The entity/document type being paginated.</typeparam>
public abstract class SeekBuilderCore<T>
{
    private readonly ISeekSerializer _serializer;
    private readonly ISeekValueConverter _valueConverter;
    private readonly SeekOptionsBase _options;

    private SeekRequest? _request;
    private readonly List<ISortColumn<T>> _sortColumns = [];

    protected SeekBuilderCore(
        ISeekSerializer serializer,
        ISeekValueConverter valueConverter,
        SeekOptionsBase options)
    {
        _serializer     = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _valueConverter = valueConverter ?? throw new ArgumentNullException(nameof(valueConverter));
        _options        = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>The value converter used to encode/decode keyset values.</summary>
    protected ISeekValueConverter ValueConverter => _valueConverter;

    /// <summary>The sort columns registered so far, in priority order.</summary>
    protected IReadOnlyList<ISortColumn<T>> SortColumns => _sortColumns;

    /// <summary>Sets the pagination request (token + page size).</summary>
    protected void SetRequest(SeekRequest request)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
    }

    /// <summary>Adds a sort column. Column order determines sort priority.</summary>
    protected void AddSortColumn<TKey>(Expression<Func<T, TKey>> keySelector, bool isDescending)
    {
        if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
        var propertyPath = GetPropertyPath(keySelector);
        _sortColumns.Add(new SortColumn<T, TKey>(propertyPath, keySelector, isDescending));
    }

    /// <summary>
    /// Fetches up to <paramref name="limit"/> rows, ordered by <see cref="SortColumns"/>
    /// (direction inverted for <see cref="SeekDirection.Previous"/>) and filtered by the
    /// keyset boundary in <paramref name="seekData"/> when present.
    /// </summary>
    /// <param name="seekData">The decoded cursor, or <c>null</c> for the first page (no keyset filter).</param>
    /// <param name="direction">Navigation direction, controlling sort inversion.</param>
    /// <param name="limit">Maximum rows to return (page size + 1 for look-ahead).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected abstract Task<List<T>> FetchAsync(
        SeekData? seekData,
        SeekDirection direction,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs the full keyset pagination pipeline and returns the page result.
    /// </summary>
    protected async ValueTask<SeekResult<T>> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (_sortColumns.Count == 0)
            throw new InvalidOperationException("At least one order field is required. Use OrderBy() or OrderByDescending().");

        SeekDirection seekDirection = SeekDirection.Next;
        SeekData? seekData = null;

        _request ??= new SeekRequest { PageSize = _options.DefaultPageSize };

        bool isNullToken = string.IsNullOrWhiteSpace(_request.Token);

        if (!isNullToken)
        {
            seekData = _serializer.Deserialize(_request.Token!);
            seekDirection = seekData.Direction;
        }

        var pageSize = _request.PageSize.GetValueOrDefault(_options.DefaultPageSize);
        pageSize = Math.Clamp(pageSize, _options.MinPageSize, _options.MaxPageSize);

        // A cursor with no values (malformed/first-page fallback) applies no filter.
        var effectiveSeekData = seekData is { Values.Count: > 0 } ? seekData : null;

        // Look-ahead: fetch one extra row to detect whether a further page exists.
        var items = await FetchAsync(effectiveSeekData, seekDirection, pageSize + 1, cancellationToken);

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
            : !isNullToken;

        var hasPrevious = seekDirection == SeekDirection.Next
            ? !isNullToken
            : hasMore;

        return new SeekResult<T>
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

    private static string GetPropertyPath<TKey>(Expression<Func<T, TKey>> expression)
    {
        return GetPropertyPathInternal(expression.Body);
    }

    private static string GetPropertyPathInternal(Expression expression)
    {
        switch (expression)
        {
            case MemberExpression memberExpr:
                // Simple: o => o.Name
                if (memberExpr.Expression is ParameterExpression)
                    return memberExpr.Member.Name;

                // Nested: o => o.Customer.Name
                var basePath = GetPropertyPathInternal(memberExpr.Expression!);
                return $"{basePath}.{memberExpr.Member.Name}";

            case UnaryExpression { Operand: MemberExpression memberExpr2 }:
                return GetPropertyPathInternal(memberExpr2);

            default:
                throw new ArgumentException($"Expression '{expression}' must be a property access");
        }
    }

    private string CreateCursor(T item, SeekDirection direction)
    {
        var stringValues = new Dictionary<string, string>(_sortColumns.Count);

        foreach (var field in _sortColumns)
        {
            var value = field.GetValue(item);
            var stringValue = _valueConverter.ToString(value, field.KeyType);
            stringValues[field.PropertyPath] = stringValue ?? string.Empty;
        }

        return _serializer.Serialize(new SeekData
        {
            Values = stringValues,
            Direction = direction,
        });
    }
}
