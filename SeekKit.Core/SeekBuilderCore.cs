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

    /// <summary>The token serializer used to encode/decode cursor tokens.</summary>
    protected ISeekSerializer Serializer => _serializer;

    /// <summary>The pagination request (token + page size) set via <see cref="SetRequest"/>, if any.</summary>
    protected SeekRequest? Request => _request;

    /// <summary>The sort columns registered so far, in priority order.</summary>
    protected IReadOnlyList<ISortColumn<T>> SortColumns => _sortColumns;

    /// <summary>Sets the pagination request (token + page size).</summary>
    protected void SetRequest(SeekRequest request)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
    }

    /// <summary>
    /// Adds a sort column. Column order determines sort priority.
    /// </summary>
    /// <param name="keySelector">Expression selecting the column.</param>
    /// <param name="isDescending"><see langword="true"/> for descending, <see langword="false"/> for ascending.</param>
    /// <param name="resultPropertyName">
    /// Overrides the cursor's key name for this column instead of deriving it from
    /// <paramref name="keySelector"/>. Required when <paramref name="keySelector"/> is an
    /// identity selector (<c>x =&gt; x</c>, e.g. sorting an <c>IQueryable&lt;int&gt;</c> by
    /// itself) and the query is later projected via <c>Select()</c> — there's no property
    /// name to derive, so the resulting <c>TResult</c> shape's matching property name (e.g.
    /// <c>"Id"</c>) must be supplied explicitly so SeekKit can read the cursor value back
    /// after projection.
    /// </param>
    protected void AddSortColumn<TKey>(Expression<Func<T, TKey>> keySelector, bool isDescending, string? resultPropertyName = null)
    {
        if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
        var propertyPath = resultPropertyName ?? GetPropertyPath(keySelector);
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

        _request ??= new SeekRequest { PageSize = _options.DefaultPageSize };

        return await SeekPagingAlgorithm.ExecuteAsync(_request, _options, _serializer, FetchAsync, CreateCursor, cancellationToken);
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

            // Identity: x => x — sorting a scalar sequence (e.g. IQueryable<int>)
            // by the element itself, with no property to access.
            case ParameterExpression:
            case UnaryExpression { Operand: ParameterExpression }:
                return SeekIdentitySortColumn.PropertyPath;

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
