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

/// <summary>
/// Sentinel <see cref="ISortColumn{T}.PropertyPath"/> assigned to an identity sort
/// selector (<c>x =&gt; x</c>) — sorting a scalar sequence (e.g. <c>IQueryable&lt;int&gt;</c>)
/// by the element itself, where there is no property to name. Not a real property name
/// (C# identifiers can't contain <c>$</c>), so it can never accidentally match a
/// <c>TResult</c> property in <c>Select()</c>'s projected-cursor lookup.
/// </summary>
internal static class SeekIdentitySortColumn
{
    internal const string PropertyPath = "$self";
}

/// <summary>
/// Naming for sort columns whose selector is a computed expression
/// (<c>x =&gt; x.Name.ToLower()</c>, <c>x =&gt; x.Price * x.Quantity</c>) rather than a property.
/// There is no property name to use as the cursor key, so the column is keyed by its
/// position in the sort: <c>$expr0</c>, <c>$expr1</c>, … Like the identity sentinel, the
/// <c>$</c> keeps it from ever colliding with a real property name.
/// </summary>
internal static class SeekComputedSortColumn
{
    private const string Prefix = "$expr";

    internal static string PropertyPath(int position)
        => Prefix + position.ToString(CultureInfo.InvariantCulture);

    internal static bool IsComputed(string propertyPath)
        => propertyPath.StartsWith(Prefix, StringComparison.Ordinal);
}

internal sealed class SortColumn<T, TKey> : ISortColumn<T>
{
    // Compiled accessors for plain member chains (x => x.CreatedAt, x => x.Customer.Name),
    // keyed by the chain. A builder is created per request, and compiling the selector
    // there cost ~60 µs per sort column per request; this way each distinct column is
    // compiled once per process.
    private static readonly ConcurrentDictionary<string, Func<T, object?>> MemberAccessors = new(StringComparer.Ordinal);

    private readonly Expression<Func<T, TKey>> _keySelector;
    private readonly Type _valueType;
    private Func<T, object?>? _accessor;

    public SortColumn(string name, Expression<Func<T, TKey>> keySelector, bool isDescending)
    {
        PropertyPath = name;
        _keySelector = keySelector;
        IsDescending = isDescending;
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
        // Resolved on first use: a projected (Select) query never reads the sort value
        // from T at all, and an empty page needs no cursor.
        return (_accessor ??= ResolveAccessor())(entity);
    }

    private Func<T, object?> ResolveAccessor()
    {
        var parameter = _keySelector.Parameters[0];
        var body = _keySelector.Body;

        // Identity: x => x
        if (body == parameter)
            return static entity => entity;

        // A bare member chain rooted at the parameter is fully described by its member
        // names, so its compiled accessor can be shared. Anything with a conversion or a
        // computation in it is not, and is handled below.
        string? chain = null;
        var current = body;
        while (current is MemberExpression member)
        {
            chain = chain is null ? member.Member.Name : $"{member.Member.Name}.{chain}";
            current = member.Expression;
        }

        if (chain is not null && current == parameter)
            return MemberAccessors.GetOrAdd(chain, static (_, selector) => Box(selector).Compile(), _keySelector);

        // Computed expression: it differs per call site, so there is nothing to share.
        // It runs at most twice per page (first and last row) — interpreting it is far
        // cheaper than emitting and JIT-compiling a delegate for every request.
        return Box(_keySelector).Compile(preferInterpretation: true);
    }

    private static Expression<Func<T, object?>> Box(Expression<Func<T, TKey>> selector)
        => Expression.Lambda<Func<T, object?>>(
            Expression.Convert(selector.Body, typeof(object)),
            selector.Parameters);
}