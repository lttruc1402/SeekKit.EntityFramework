namespace SeekKit.Core.Strategies;

/// <summary>
/// Keyset filter built from OR-expanded predicates:
/// <c>(a &gt; @a) OR (a == @a AND b &gt; @b) OR ...</c>.
/// Works on every LINQ provider (EF Core, MongoDB driver, in-memory).
/// </summary>
public sealed class OrLogicSeekStrategy : ISeekFilterStrategy
{
    private readonly ISeekValueConverter _valueConverter;

    public OrLogicSeekStrategy(ISeekValueConverter valueConverter)
    {
        _valueConverter = valueConverter;
    }

    public IQueryable<T> ApplyFilter<T>(IQueryable<T> query, ParameterExpression parameter, IReadOnlyList<ISortColumn<T>> orderFields, SeekData seekData)
    {
        var body = KeysetPredicateBuilder.BuildOrPredicateBody(parameter, orderFields, seekData, _valueConverter);

        return body is null
            ? query
            : query.Where(Expression.Lambda<Func<T, bool>>(body, parameter));
    }
}
