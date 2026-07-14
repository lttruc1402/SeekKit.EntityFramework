namespace SeekKit.Core.Helpers;

/// <summary>
/// Applies the keyset sort columns to a queryable, inverting direction for
/// backward pagination.
/// </summary>
internal static class QueryableOrderingHelper
{
    internal static IQueryable<T> ApplyOrdering<T>(this IQueryable<T> query, IReadOnlyList<ISortColumn<T>> sortColumns, SeekDirection direction)
    {
        bool shouldReverse = direction == SeekDirection.Previous;
        IOrderedQueryable<T>? ordered = null;

        for (int i = 0; i < sortColumns.Count; i++)
        {
            var field = sortColumns[i];

            var isDescending = shouldReverse
                ? !field.IsDescending
                : field.IsDescending;

            ordered = ordered is null
                ? field.ApplyOrderBy(query, isDescending)
                : field.ApplyThenBy(ordered, isDescending);
        }

        return ordered!;
    }
}
