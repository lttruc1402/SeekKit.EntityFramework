namespace SeekKit.EntityFramework.Core;

/// <summary>
/// Defines a strategy for applying a keyset filter to an <see cref="IQueryable{T}"/>.
/// Implement this interface to provide a custom pagination filter.
/// Built-in implementations: <c>UnionAllSeekStrategy</c>, <c>OrLogicSeekStrategy</c>,
/// <c>PostgreSqlTupleSeekStrategy</c>.
/// </summary>
public interface ISeekFilterStrategy
{
    /// <summary>
    /// Applies the keyset filter to <paramref name="query"/> based on the boundary values
    /// stored in <paramref name="seekData"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The base query to filter.</param>
    /// <param name="parameter">The LINQ parameter expression representing a single entity.</param>
    /// <param name="sortColumns">Ordered list of sort columns that define the keyset.</param>
    /// <param name="seekData">The decoded token containing boundary values and direction.</param>
    IQueryable<T> ApplyFilter<T>(
        IQueryable<T> query,
        ParameterExpression parameter,
        List<ISortColumn<T>> sortColumns,
        SeekData seekData);
}
