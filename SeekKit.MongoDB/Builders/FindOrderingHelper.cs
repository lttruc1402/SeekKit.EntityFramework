namespace SeekKit.MongoDB.Builders;

/// <summary>
/// Applies the keyset sort columns to an <see cref="IFindFluent{T, T}"/> using
/// the driver's <c>SortBy</c>/<c>ThenBy</c> extensions, inverting direction for
/// backward pagination.
/// </summary>
internal static class FindOrderingHelper
{
    internal static IFindFluent<T, T> ApplyOrdering<T>(
        IFindFluent<T, T> find,
        IReadOnlyList<ISortColumn<T>> sortColumns,
        SeekDirection direction)
    {
        bool reverse = direction == SeekDirection.Previous;
        IOrderedFindFluent<T, T>? ordered = null;

        for (int i = 0; i < sortColumns.Count; i++)
        {
            var column     = sortColumns[i];
            var descending = reverse ? !column.IsDescending : column.IsDescending;
            var selector   = MongoSortSelector.ToObjectSelector<T>(column.KeySelector);

            ordered = ordered is null
                ? (descending ? find.SortByDescending(selector) : find.SortBy(selector))
                : (descending ? ordered.ThenByDescending(selector) : ordered.ThenBy(selector));
        }

        return ordered!;
    }
}
