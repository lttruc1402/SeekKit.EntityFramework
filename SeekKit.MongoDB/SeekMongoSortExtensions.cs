namespace SeekKit.MongoDB;

/// <summary>
/// Dynamic, client-driven sorting for the MongoDB builders.
/// </summary>
public static class SeekMongoSortExtensions
{
    /// <summary>
    /// Sorts by a client-supplied sort string (e.g. <c>"price:desc,name:asc"</c>), restricted to
    /// the fields whitelisted on <paramref name="sortMap"/>. Equivalent to calling
    /// <c>OrderBy</c> / <c>OrderByDescending</c> once per requested field, followed by the
    /// map's tie-breaker.
    /// </summary>
    /// <param name="builder">The builder to add the sort columns to.</param>
    /// <param name="sortMap">The whitelist of sortable fields, its default sort and tie-breaker.</param>
    /// <param name="sort">The raw sort string from the request; <see langword="null"/> or empty uses the map's default.</param>
    /// <exception cref="SeekSortException">
    /// The sort string names an unknown field or is malformed — answer with <c>400 Bad Request</c>.
    /// </exception>
    public static ISeekMongoBuilder<T> ApplySort<T>(this ISeekMongoBuilder<T> builder, SeekSortMap<T> sortMap, string? sort)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (sortMap is null) throw new ArgumentNullException(nameof(sortMap));

        sortMap.Apply(sort, new BuilderTarget<T>(builder));
        return builder;
    }

    /// <inheritdoc cref="ApplySort{T}(ISeekMongoBuilder{T}, SeekSortMap{T}, string)"/>
    public static ISeekMongoQueryableBuilder<T> ApplySort<T>(this ISeekMongoQueryableBuilder<T> builder, SeekSortMap<T> sortMap, string? sort)
    {
        ApplySort((ISeekMongoBuilder<T>)builder, sortMap, sort);
        return builder;
    }

    /// <inheritdoc cref="ApplySort{T}(ISeekMongoBuilder{T}, SeekSortMap{T}, string)"/>
    public static ISeekMongoAggregateBuilder<T> ApplySort<T>(this ISeekMongoAggregateBuilder<T> builder, SeekSortMap<T> sortMap, string? sort)
    {
        ApplySort((ISeekMongoBuilder<T>)builder, sortMap, sort);
        return builder;
    }

    /// <inheritdoc cref="ApplySort{T}(ISeekMongoBuilder{T}, SeekSortMap{T}, string)"/>
    public static ISeekMongoFindBuilder<T> ApplySort<T>(this ISeekMongoFindBuilder<T> builder, SeekSortMap<T> sortMap, string? sort)
    {
        ApplySort((ISeekMongoBuilder<T>)builder, sortMap, sort);
        return builder;
    }

    private sealed class BuilderTarget<T> : ISeekSortTarget<T>
    {
        private readonly ISeekMongoBuilder<T> _builder;

        public BuilderTarget(ISeekMongoBuilder<T> builder) => _builder = builder;

        public void OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, bool descending, string? resultPropertyName)
        {
            if (descending)
                _builder.OrderByDescending(keySelector, resultPropertyName);
            else
                _builder.OrderBy(keySelector, resultPropertyName);
        }
    }
}
