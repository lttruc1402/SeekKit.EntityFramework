namespace SeekKit.Core.Helpers;

/// <summary>
/// Internal extension methods on <see cref="ISortColumn{T}"/> and lists of them that
/// support the filter-strategy implementations with common LINQ expression helpers.
/// </summary>
internal static class ISortColumnHelper
{
    private static readonly MethodInfo StringCompare =
        typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!;

    /// <summary>
    /// Determines whether the column list satisfies the prerequisites for a
    /// native SQL row-value / tuple comparison (<c>(a, b) &gt; (c, d)</c>).
    /// </summary>
    /// <remarks>
    /// Three conditions must all hold:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///     The column count is between 2 and 8 inclusive — the maximum
    ///     supported by the ValueTuple family (<c>ValueTuple&lt;T1,…,T8&gt;</c>).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     All columns share the same sort direction (all ascending <b>or</b>
    ///     all descending). Mixed directions cannot be expressed as a single
    ///     tuple inequality.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     No column can hold <c>NULL</c>. A row-value comparison involving a
    ///     <c>NULL</c> evaluates to unknown, so rows with a null sort value would
    ///     silently drop out of every page after the first.
    ///     </description>
    ///   </item>
    /// </list>
    /// When this returns <see langword="false"/>, the caller must fall back to
    /// an OR-logic or UNION ALL strategy.
    /// </remarks>
    public static bool CanUseTuple<T>(this IReadOnlyList<ISortColumn<T>> sortColumns)
    {
        if (sortColumns.Count is > 8 or < 2)
            return false;

        if (!(sortColumns.All(f => !f.IsDescending) || sortColumns.All(f => f.IsDescending)))
            return false;

        return !sortColumns.Any(SortColumnNullability.CanBeNull);
    }

    /// <summary>
    /// Converts a raw cursor value dictionary into a <see cref="SeekValueHolder"/>
    /// dictionary keyed by <see cref="ISortColumn{T}.PropertyPath"/>.
    /// </summary>
    /// <remarks>
    /// Each cursor string is deserialised once via <see cref="ISeekValueConverter.FromString"/>
    /// and wrapped in a <see cref="SeekValueHolder"/> whose boxed <c>Value</c> is shared by
    /// reference across all LINQ expression trees built for the same page request.
    /// </remarks>
    /// <exception cref="SeekTokenException">
    /// The token carries no value for one of the columns (it was issued for another sort),
    /// or a value can't be converted to its column's type.
    /// </exception>
    public static Dictionary<string, SeekValueHolder> ToSeekValueHolders<T>(this IReadOnlyList<ISortColumn<T>> sortColumns, Dictionary<string, string> cursorValues, ISeekValueConverter valueConverter)
    {
        var valueHolders = new Dictionary<string, SeekValueHolder>(cursorValues.Count, StringComparer.Ordinal);

        foreach (var field in sortColumns)
        {
            if (!cursorValues.TryGetValue(field.PropertyPath, out string? cursorString))
            {
                throw new SeekTokenException(SeekTokenError.SortMismatch,
                    $"The pagination token carries no value for sort column '{field.PropertyPath}' — " +
                    "it was issued for a different sort order than this request uses.");
            }

            object? typedValue;
            if (cursorString is null)
            {
                typedValue = null;
            }
            else
            {
                try
                {
                    typedValue = valueConverter.FromString(cursorString, field.KeyType);
                }
                catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or InvalidCastException)
                {
                    throw new SeekTokenException(SeekTokenError.InvalidValue,
                        $"The pagination token holds a value for sort column '{field.PropertyPath}' that is not a valid {field.KeyType.Name}.", ex);
                }
            }

            if (typedValue is null && field.KeyType.IsValueType && Nullable.GetUnderlyingType(field.KeyType) is null)
            {
                throw new SeekTokenException(SeekTokenError.InvalidValue,
                    $"The pagination token holds no value for sort column '{field.PropertyPath}', which cannot be null.");
            }

            valueHolders[field.PropertyPath] = new SeekValueHolder { Value = typedValue };
        }

        return valueHolders;
    }

    /// <summary>
    /// Builds <c>column == cursorValue</c> — the tie anchor used for every column that
    /// precedes the one a keyset level compares on. A <c>NULL</c> boundary value becomes
    /// <c>column == null</c> (<c>IS NULL</c>).
    /// </summary>
    public static Expression CreateEqualCondition<T>(this ISortColumn<T> sortColumn, ParameterExpression parameter, Dictionary<string, SeekValueHolder> sharedValueHolders)
    {
        var (property, holder) = Resolve(sortColumn, parameter, sharedValueHolders);

        return holder.Value is null
            ? Expression.Equal(property, Expression.Constant(null, property.Type))
            : Expression.Equal(property, BoundaryValue(holder, property.Type));
    }

    /// <summary>
    /// Builds the condition "this column's value comes strictly after the cursor's, in
    /// the direction being scanned", or returns <see langword="null"/> when no value can.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a column that can't hold <c>NULL</c> this is the plain <c>column &gt; value</c>
    /// (or <c>&lt;</c>). For one that can, <c>NULL</c> has to be accounted for by hand,
    /// because a comparison against it is never true in SQL or MongoDB:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///   boundary is a real value and nulls lie <b>ahead</b> in the scan —
    ///   <c>column &gt; value OR column IS NULL</c>;
    ///   </description></item>
    ///   <item><description>
    ///   boundary is a real value and nulls lie <b>behind</b> — <c>column &gt; value</c>;
    ///   </description></item>
    ///   <item><description>
    ///   boundary is <c>NULL</c> and nulls lie <b>behind</b> the real values —
    ///   <c>column IS NOT NULL</c>;
    ///   </description></item>
    ///   <item><description>
    ///   boundary is <c>NULL</c> and nulls are the <b>end</b> of the scan — nothing comes
    ///   after, so <see langword="null"/> is returned and the level is dropped.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Which side the nulls are on follows from the column's direction, the navigation
    /// direction, and <paramref name="nullOrdering"/> (where the database itself puts
    /// them) — the <c>ORDER BY</c> is never altered.
    /// </para>
    /// </remarks>
    // nullAlternative: the "column IS NULL" half of the first case above, handed out
    // separately instead of being OR-ed into the result. The two halves match disjoint
    // rows, so a caller may OR them back together, or run each as its own index range
    // scan — which is what keeps a UNION ALL branch sargable: an OR inside it makes
    // PostgreSQL read and sort everything beyond the boundary (measured: 1.2 s instead
    // of 2 ms at the middle of a 2M-row table). Null when there is no such half.
    public static Expression? CreateAfterCondition<T>(this ISortColumn<T> sortColumn, ParameterExpression parameter, SeekDirection direction, Dictionary<string, SeekValueHolder> sharedValueHolders, SeekNullOrdering nullOrdering, out Expression? nullAlternative)
    {
        nullAlternative = null;
        var (property, holder) = Resolve(sortColumn, parameter, sharedValueHolders);

        // Ascending scan: forward over an ascending column, or backward over a descending one.
        bool scanAscending = (direction == SeekDirection.Next) != sortColumn.IsDescending;

        if (!SortColumnNullability.CanBeNull(sortColumn))
            return Compare(property, BoundaryValue(holder, property.Type), scanAscending);

        bool nullsAhead = scanAscending == (nullOrdering == SeekNullOrdering.NullsLargest);
        var nullConstant = Expression.Constant(null, property.Type);

        if (holder.Value is null)
        {
            return nullsAhead
                ? null
                : Expression.NotEqual(property, nullConstant);
        }

        if (nullsAhead)
            nullAlternative = Expression.Equal(property, nullConstant);

        return Compare(property, BoundaryValue(holder, property.Type), scanAscending);
    }

    private static (Expression Property, SeekValueHolder Holder) Resolve<T>(ISortColumn<T> sortColumn, ParameterExpression parameter, Dictionary<string, SeekValueHolder> sharedValueHolders)
    {
        if (!sharedValueHolders.TryGetValue(sortColumn.PropertyPath, out var valueHolder))
            throw new InvalidOperationException($"Cursor value for '{sortColumn.PropertyPath}' not found");

        var selector = sortColumn.KeySelector;
        var property = new ParameterReplaceVisitor(selector.Parameters[0], parameter).Visit(selector.Body);
        return (property, valueHolder);
    }

    // Read through the shared holder rather than inlined as a constant, so every
    // expression tree built for one page request refers to the same object and the
    // provider can parameterise it.
    private static Expression BoundaryValue(SeekValueHolder holder, Type type)
        => Expression.Convert(
            Expression.Property(Expression.Constant(holder), nameof(SeekValueHolder.Value)),
            type);

    private static Expression Compare(Expression left, Expression right, bool greaterThan)
    {
        // string has no > / < operators; string.Compare(a, b) > 0 is the form LINQ
        // providers translate to a plain column comparison.
        if (left.Type == typeof(string))
        {
            var compare = Expression.Call(StringCompare, left, right);
            var zero = Expression.Constant(0);
            return greaterThan
                ? Expression.GreaterThan(compare, zero)
                : Expression.LessThan(compare, zero);
        }

        return greaterThan
            ? Expression.GreaterThan(left, right)
            : Expression.LessThan(left, right);
    }
}
