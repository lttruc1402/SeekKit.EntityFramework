namespace SeekKit.Core.Models;

/// <summary>
/// Represents a single sort column in a keyset (cursor) pagination query.
/// Each column contributes one level of ordering and provides its boundary
/// value for cursor encoding.
/// </summary>
/// <remarks>
/// <para>
/// Instances are created internally by the provider builders'
/// <c>OrderBy</c> and <c>OrderByDescending</c> methods
/// (SeekKit.EntityFramework's <c>ISeekBuilder&lt;T&gt;</c>,
/// SeekKit.MongoDB's <c>ISeekMongoBuilder&lt;T&gt;</c>).
/// </para>
/// <para>
/// Implement this interface only when you need to inject a fully custom sort
/// column into a custom <see cref="ISeekFilterStrategy"/>.
/// </para>
/// </remarks>
/// <typeparam name="T">The entity type being queried.</typeparam>
public interface ISortColumn<T>
{
    /// <summary>
    /// The dot-separated property path used as the stable key in the cursor
    /// token (e.g. <c>"Id"</c>, <c>"Customer.Name"</c>).
    /// Must be unique within a single builder's column list.
    /// </summary>
    string PropertyPath { get; }

    /// <summary>
    /// The lambda expression that selects this column's value from an entity
    /// (e.g. <c>x =&gt; x.CreatedAt</c>).
    /// Used to build the LINQ ordering and filter expressions.
    /// </summary>
    LambdaExpression KeySelector { get; }

    /// <summary>
    /// <see langword="true"/> when this column sorts in descending order;
    /// <see langword="false"/> for ascending.
    /// </summary>
    bool IsDescending { get; }

    /// <summary>
    /// The CLR type of the sort key (e.g. <c>typeof(int)</c>,
    /// <c>typeof(DateTime)</c>).
    /// Used to look up the correct <see cref="TypeConverter{T}"/> when
    /// encoding and decoding the cursor token.
    /// </summary>
    Type KeyType { get; }

    /// <summary>
    /// Applies <c>OrderBy</c> or <c>OrderByDescending</c> to
    /// <paramref name="query"/> according to <paramref name="isDescending"/>.
    /// Called for the first (primary) sort column.
    /// </summary>
    /// <param name="query">The base queryable to order.</param>
    /// <param name="isDescending">
    /// <see langword="true"/> to sort descending; <see langword="false"/> for
    /// ascending. May differ from <see cref="IsDescending"/> when the sort
    /// direction is inverted for backward pagination.
    /// </param>
    /// <returns>The queryable with the primary ordering applied.</returns>
    IOrderedQueryable<T> ApplyOrderBy(IQueryable<T> query, bool isDescending);

    /// <summary>
    /// Applies <c>ThenBy</c> or <c>ThenByDescending</c> to an
    /// already-ordered <paramref name="query"/> according to
    /// <paramref name="isDescending"/>.
    /// Called for every sort column after the first.
    /// </summary>
    /// <param name="query">The already-ordered queryable to extend.</param>
    /// <param name="isDescending">
    /// <see langword="true"/> to sort descending; <see langword="false"/> for
    /// ascending.
    /// </param>
    /// <returns>The queryable with the additional ordering applied.</returns>
    IOrderedQueryable<T> ApplyThenBy(IOrderedQueryable<T> query, bool isDescending);

    /// <summary>
    /// Extracts this column's value from <paramref name="entity"/> as a
    /// boxed <see cref="object"/>.
    /// Used when building the cursor token from the last item on the current
    /// page.
    /// </summary>
    /// <param name="entity">The entity instance to read from.</param>
    /// <returns>
    /// The column's value, or <see langword="null"/> if the property value is
    /// <see langword="null"/>.
    /// </returns>
    object? GetValue(T entity);
}

internal sealed class SortColumn<T, TKey> : ISortColumn<T>
{
    private readonly Expression<Func<T, TKey>> _keySelector;
    private readonly Func<T, TKey> _func;
    private readonly Type _valueType;
    public SortColumn(string name, Expression<Func<T, TKey>> keySelector, bool isDescending)
    {
        PropertyPath = name;
        _keySelector = keySelector;
        IsDescending = isDescending;

        _func = _keySelector.Compile();
        _valueType = typeof(TKey);
    }

    public string PropertyPath { get; }

    public LambdaExpression KeySelector => _keySelector;

    public bool IsDescending { get; }

    public Type KeyType => _valueType;

    public IOrderedQueryable<T> ApplyOrderBy(IQueryable<T> query, bool isDescending)
    {
        return isDescending
                    ? query.OrderByDescending(_keySelector)
                    : query.OrderBy(_keySelector);
    }

    public IOrderedQueryable<T> ApplyThenBy(IOrderedQueryable<T> query, bool isDescending)
    {
        return isDescending
                     ? query.ThenByDescending(_keySelector)
                     : query.ThenBy(_keySelector);
    }

    public object? GetValue(T entity)
    {
        return _func(entity);
    }
}