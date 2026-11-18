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
    private bool _includeTotalCount;
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

    /// <summary>Whether the result should carry the total number of rows (see <see cref="SetIncludeTotalCount"/>).</summary>
    protected bool IncludeTotalCount => _includeTotalCount;

    /// <summary>Sets the pagination request (token + page size).</summary>
    protected void SetRequest(SeekRequest request)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
    }

    /// <summary>
    /// Turns the total count on or off. When on, <see cref="CountAsync"/> runs once per
    /// page request (once per walk for <see cref="ExecutePagesAsync"/>) and its result is
    /// returned as <see cref="SeekResult{T}.TotalCount"/>.
    /// </summary>
    protected void SetIncludeTotalCount(bool include)
    {
        _includeTotalCount = include;
    }

    /// <summary>
    /// Adds a sort column. Column order determines sort priority.
    /// </summary>
    /// <param name="keySelector">
    /// Expression selecting the column: a property (<c>x =&gt; x.CreatedAt</c>), the element
    /// itself (<c>x =&gt; x</c>), or a computed expression the provider can translate
    /// (<c>x =&gt; x.Name.ToLower()</c>).
    /// </param>
    /// <param name="isDescending"><see langword="true"/> for descending, <see langword="false"/> for ascending.</param>
    /// <param name="resultPropertyName">
    /// Overrides the cursor's key name for this column instead of deriving it from
    /// <paramref name="keySelector"/> (a computed expression gets a positional name,
    /// <c>$expr0</c>, <c>$expr1</c>, …); after a <c>Select()</c> projection it is also the
    /// property the cursor value is read from on the projected <c>TResult</c>. Optional:
    /// projection builders derived from <c>SeekProjectionBuilderBase</c> otherwise trace
    /// the value through the transformer, so it is only needed where tracing can't work —
    /// a computed value, the optional side of an outer join, or a provider that builds its
    /// projection outside LINQ expression trees.
    /// </param>
    protected void AddSortColumn<TKey>(Expression<Func<T, TKey>> keySelector, bool isDescending, string? resultPropertyName = null)
    {
        if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
        var propertyPath = resultPropertyName ?? GetPropertyPath(keySelector, _sortColumns.Count);
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
    /// Counts every row the query would return without pagination — no keyset filter, no
    /// limit. Called only when the total count was requested via
    /// <see cref="SetIncludeTotalCount"/>. The default implementation throws; builders
    /// that support a total override it.
    /// </summary>
    protected virtual Task<long> CountAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException($"{GetType().Name} does not support a total count.");

    /// <summary>
    /// Runs the full keyset pagination pipeline and returns the page result.
    /// </summary>
    protected ValueTask<SeekResult<T>> ExecuteAsync(CancellationToken cancellationToken)
        => CreatePager().PageAsync(_request, cancellationToken);

    /// <summary>
    /// Returns the requested page and then every following one, fetching each lazily as
    /// the sequence is enumerated.
    /// </summary>
    protected IAsyncEnumerable<SeekResult<T>> ExecutePagesAsync(CancellationToken cancellationToken)
        => CreatePager().PagesAsync(_request, cancellationToken);

    /// <summary>
    /// Like <see cref="ExecutePagesAsync"/>, flattened to the individual items.
    /// </summary>
    protected IAsyncEnumerable<T> ExecuteItemsAsync(CancellationToken cancellationToken)
        => CreatePager().ItemsAsync(_request, cancellationToken);

    private SeekPager<T, T> CreatePager()
    {
        // Snapshot: columns added to the builder later must not leak into a walk in progress.
        var columns = _sortColumns.ToArray();

        return new SeekPager<T, T>(
            columns,
            _serializer,
            _valueConverter,
            _options,
            FetchAsync,
            (item, index) => columns[index].GetValue(item),
            _includeTotalCount ? CountAsync : null);
    }

    private static string GetPropertyPath<TKey>(Expression<Func<T, TKey>> expression, int position)
    {
        return TryGetPropertyPath(expression.Body, expression.Parameters[0])
            ?? SeekComputedSortColumn.PropertyPath(position);
    }

    /// <summary>
    /// Returns the dot-separated member path of a plain property access, the identity
    /// sentinel for <c>x =&gt; x</c>, or <see langword="null"/> for anything computed.
    /// </summary>
    private static string? TryGetPropertyPath(Expression expression, ParameterExpression parameter)
    {
        // A conversion on the outside (o => (long)o.Id, o => (object)o.Id) doesn't change
        // which column is being sorted.
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked, Method: null } convert)
            expression = convert.Operand;

        // Identity: x => x — sorting a scalar sequence (e.g. IQueryable<int>)
        // by the element itself, with no property to access.
        if (expression == parameter)
            return SeekIdentitySortColumn.PropertyPath;

        // Simple: o => o.Name      Nested: o => o.Customer.Name
        string? path = null;
        while (expression is MemberExpression member)
        {
            path = path is null ? member.Member.Name : $"{member.Member.Name}.{path}";
            expression = member.Expression!;
        }

        return path is not null && expression == parameter ? path : null;
    }
}
