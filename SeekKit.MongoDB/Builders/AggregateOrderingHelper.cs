namespace SeekKit.MongoDB.Builders;

/// <summary>
/// Applies the keyset sort columns to an <see cref="IAggregateFluent{T}"/> using
/// the driver's <c>SortBy</c>/<c>ThenBy</c> extensions, inverting direction for
/// backward pagination.
/// </summary>
internal static class AggregateOrderingHelper
{
    internal static IAggregateFluent<T> ApplyOrdering<T>(
        IAggregateFluent<T> pipeline,
        IReadOnlyList<ISortColumn<T>> sortColumns,
        SeekDirection direction)
    {
        bool reverse = direction == SeekDirection.Previous;
        IOrderedAggregateFluent<T>? ordered = null;

        for (int i = 0; i < sortColumns.Count; i++)
        {
            var column     = sortColumns[i];
            var descending = reverse ? !column.IsDescending : column.IsDescending;
            var selector   = MongoSortSelector.ToObjectSelector<T>(column.KeySelector);

            ordered = ordered is null
                ? (descending ? pipeline.SortByDescending(selector) : pipeline.SortBy(selector))
                : (descending ? ordered.ThenByDescending(selector) : ordered.ThenBy(selector));
        }

        return ordered!;
    }
}
