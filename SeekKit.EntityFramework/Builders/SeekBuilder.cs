namespace SeekKit.EntityFramework.Builders;

internal sealed class SeekBuilder<T> : ISeekBuilder<T>
{
    private readonly IQueryable<T> _query;
    private readonly ISeekSerializer _serializer;
    private readonly ISeekValueConverter _valueConverter;
    private readonly SeekKitOptions _options;
    private ISeekFilterStrategy? _cursorFilterStrategy;
    private SeekRequest? _request;
    private readonly List<ISortColumn<T>> _orderFields = [];

    public SeekBuilder(IQueryable<T> query, ISeekSerializer seekSerializer, ISeekValueConverter valueConverter, SeekKitOptions options)
    {
        _query          = query;
        _serializer     = seekSerializer;
        _valueConverter = valueConverter;
        _options        = options;
    }

    public ISeekBuilder<T> WithRequest(SeekRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        _request = request;
        return this;
    }

    public async ValueTask<SeekResult<T>> ToSeekResultAsync(CancellationToken cancellationToken = default)
    {
        if (_orderFields.Count == 0)
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

        IQueryable<T> query = _query.ApplyOrdering(_orderFields, seekDirection);

        if(seekData is not null)
        {
            if (_cursorFilterStrategy is null)
                _cursorFilterStrategy = _options.Strategy.GetFilterStrategy(_valueConverter);

            if (_cursorFilterStrategy is IPageSizeAware pageSizeFilter)
                pageSizeFilter.SetPageSize(pageSize + 1);

            query = _cursorFilterStrategy.ApplyFilter(query, Expression.Parameter(typeof(T), "e"), _orderFields, seekData);
        }
        // Execute
        var items = await query
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

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

    public ISeekBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
        var propertyPath = GetPropertyPath(keySelector);

        _orderFields.Add(new SortColumn<T, TKey>(propertyPath, keySelector, false));
        return this;
    }

    public ISeekBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
        var propertyPath = GetPropertyPath(keySelector);

        _orderFields.Add(new SortColumn<T, TKey>(propertyPath, keySelector, true));
        return this;
    }

    public ISeekBuilder<T> WithStrategy(ISeekFilterStrategy strategy)
    {
        _cursorFilterStrategy = strategy;
        return this;
    }

    public ISeekBuilder<T> WithTupleComparison()
    {
        if (_options.Strategy.DatabaseType == DatabaseType.PostgreSql)
            _cursorFilterStrategy = new PostgreSqlTupleSeekStrategy(_valueConverter, FallbackStrategy.UnionAll);

        return this;
    }

    public ISeekBuilder<T> WithOrPredicate()
    {
        _cursorFilterStrategy = new OrLogicSeekStrategy(_valueConverter);
        return this;
    }

    public ISeekBuilder<T> WithUnionAll()
    {
        _cursorFilterStrategy = new UnionAllSeekStrategy(_valueConverter, FallbackStrategy.OrLogic);
        return this;
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

    private string CreateCursor(T item, SeekDirection SeekDirection)
    {
        var stringValues = new Dictionary<string, string>(_orderFields.Count);

        foreach (var field in _orderFields)
        {
            var value = field.GetValue(item);
            var stringValue = _valueConverter.ToString(value, field.KeyType);
            stringValues[field.PropertyPath] = stringValue ?? string.Empty;
        }

        return _serializer.Serialize(new SeekData
        {
            Values = stringValues,
            Direction = SeekDirection,
        });
    }
}
