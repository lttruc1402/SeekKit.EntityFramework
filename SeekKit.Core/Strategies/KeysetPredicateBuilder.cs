namespace SeekKit.Core.Strategies;

/// <summary>
/// Builds the OR-expanded keyset predicate
/// <c>(a &gt; @a) OR (a == @a AND b &gt; @b) OR ...</c> as a LINQ expression.
/// Shared by <see cref="OrLogicSeekStrategy"/> (applied to an
/// <see cref="IQueryable{T}"/> via <c>Where</c>) and the MongoDB aggregation
/// builder (applied to an <c>IAggregateFluent&lt;T&gt;</c> via <c>Match</c>).
/// </summary>
/// <remarks>
/// Sort columns that can hold <c>NULL</c> get a null-aware comparison — see
/// <see cref="SeekData.NullOrdering"/>.
/// </remarks>
public static class KeysetPredicateBuilder
{
    /// <summary>
    /// Builds the keyset predicate as a lambda. Returns <c>null</c> when there
    /// are no sort columns (i.e. no boundary to filter by).
    /// </summary>
    public static Expression<Func<T, bool>>? BuildOrPredicate<T>(
        IReadOnlyList<ISortColumn<T>> sortColumns,
        SeekData seekData,
        ISeekValueConverter valueConverter)
    {
        var parameter = Expression.Parameter(typeof(T), "e");
        var body = BuildOrPredicateBody(parameter, sortColumns, seekData, valueConverter);
        return body is null ? null : Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    /// <summary>
    /// Builds the predicate body against an existing <paramref name="parameter"/>.
    /// Used by the <see cref="IQueryable{T}"/> path, which supplies its own parameter.
    /// </summary>
    internal static Expression? BuildOrPredicateBody<T>(
        ParameterExpression parameter,
        IReadOnlyList<ISortColumn<T>> sortColumns,
        SeekData seekData,
        ISeekValueConverter valueConverter)
    {
        if (sortColumns.Count == 0)
            return null;

        var branches = BuildBranchPredicates(parameter, sortColumns, seekData, valueConverter);

        // Every branch can be impossible only when the boundary is the very last row of
        // the scan (all its values NULL, with nulls sorted last): nothing lies beyond it.
        if (branches.Count == 0)
            return Expression.Constant(false);

        Expression predicate = branches[0].Predicate;
        for (int i = 1; i < branches.Count; i++)
            predicate = Expression.OrElse(predicate, branches[i].Predicate);

        return predicate;
    }

    /// <summary>
    /// Builds the keyset filter as a list of mutually exclusive predicates, each of which
    /// is a plain index range (equalities on leading columns plus one comparison, or
    /// one <c>IS NULL</c>, on the next). OR-ed together they form the keyset filter; the
    /// UNION ALL strategy runs each as its own branch instead, which is what lets the
    /// database answer every branch with an ordered index seek.
    /// </summary>
    /// <remarks>
    /// There is one predicate per keyset level — ties with the boundary on columns
    /// <c>0..i-1</c>, strictly after it on column <c>i</c> — except that a nullable column
    /// whose NULLs lie ahead of the boundary contributes two (<c>col &gt; @p</c> and
    /// <c>col IS NULL</c>), and a level no row can satisfy contributes none.
    /// <para>
    /// Each branch also reports how many leading sort columns it pins to a single value
    /// (by equality or by <c>IS NULL</c>). Within the branch those columns are constant,
    /// so it only needs to be ordered by the columns after them.
    /// </para>
    /// </remarks>
    internal static List<KeysetBranch> BuildBranchPredicates<T>(
        ParameterExpression parameter,
        IReadOnlyList<ISortColumn<T>> sortColumns,
        SeekData seekData,
        ISeekValueConverter valueConverter)
    {
        var branches = new List<KeysetBranch>(sortColumns.Count + 1);
        var valueHolders = sortColumns.ToSeekValueHolders(seekData.Values, valueConverter);
        var direction = seekData.Direction;
        var nullOrdering = seekData.NullOrdering;

        for (int i = 0; i < sortColumns.Count; i++)
        {
            // Comparison condition for the current field
            var afterCondition = sortColumns[i].CreateAfterCondition(
                parameter, direction, valueHolders, nullOrdering, out var nullAlternative);

            if (afterCondition is null)
                continue;

            // Equal conditions for previous fields
            Expression? prefix = null;
            for (int j = 0; j < i; j++)
            {
                var equalCondition = sortColumns[j].CreateEqualCondition(parameter, valueHolders);

                prefix = prefix == null
                    ? equalCondition
                    : Expression.AndAlso(prefix, equalCondition);
            }

            // Columns 0..i-1 are pinned; column i is a range.
            branches.Add(new KeysetBranch(prefix == null ? afterCondition : Expression.AndAlso(prefix, afterCondition), i));

            // Here column i is pinned as well (to NULL).
            if (nullAlternative is not null)
                branches.Add(new KeysetBranch(prefix == null ? nullAlternative : Expression.AndAlso(prefix, nullAlternative), i + 1));
        }

        return branches;
    }
}

/// <summary>One mutually exclusive piece of the keyset filter.</summary>
/// <param name="Predicate">The filter selecting this piece.</param>
/// <param name="PinnedColumns">How many leading sort columns the predicate fixes to a single value.</param>
internal readonly record struct KeysetBranch(Expression Predicate, int PinnedColumns);
