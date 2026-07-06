namespace SeekKit.EntityFramework.Helpers;

/// <summary>
/// Internal extension methods on <see cref="ISortColumn{T}"/> and
/// <see cref="List{T}"/> of <see cref="ISortColumn{T}"/> that support the
/// filter-strategy implementations with common LINQ expression helpers.
/// </summary>
internal static class ISortColumnHelper
{
    /// <summary>
    /// Determines whether the column list satisfies the prerequisites for a
    /// native SQL row-value / tuple comparison (<c>(a, b) &gt; (c, d)</c>).
    /// </summary>
    /// <remarks>
    /// Two conditions must both hold:
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
    /// </list>
    /// When this returns <see langword="false"/>, the caller must fall back to
    /// an OR-logic or UNION ALL strategy.
    /// </remarks>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="sortColumns">The ordered list of sort columns to evaluate.</param>
    /// <returns>
    /// <see langword="true"/> if the list qualifies for tuple comparison;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public static bool CanUseTuple<T>(this List<ISortColumn<T>> sortColumns)
    {
        if (sortColumns.Count is > 8 or < 2)
            return false;
        return sortColumns.TrueForAll(f => !f.IsDescending) || sortColumns.TrueForAll(f => f.IsDescending);
    }

    /// <summary>
    /// Converts a raw cursor value dictionary into a
    /// <see cref="SeekValueHolder"/> dictionary keyed by
    /// <see cref="ISortColumn{T}.PropertyPath"/>.
    /// </summary>
    /// <remarks>
    /// Each cursor string is deserialised once via
    /// <see cref="ISeekValueConverter.FromString"/> and wrapped in a
    /// <see cref="SeekValueHolder"/> whose boxed <c>Value</c> is shared by
    /// reference across all LINQ expression trees built for the same page
    /// request, eliminating redundant conversions.
    /// </remarks>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="sortColumns">
    /// The ordered list of sort columns that define the keyset sort key.
    /// </param>
    /// <param name="cursorValues">
    /// The raw string values decoded from the cursor token, keyed by
    /// <see cref="ISortColumn{T}.PropertyPath"/>.
    /// </param>
    /// <param name="valueConverter">
    /// The converter used to deserialise each string value back to its
    /// original CLR type.
    /// </param>
    /// <returns>
    /// A dictionary mapping each <see cref="ISortColumn{T}.PropertyPath"/> to
    /// its corresponding <see cref="SeekValueHolder"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="cursorValues"/> does not contain an entry
    /// for one of the columns in <paramref name="sortColumns"/>.
    /// </exception>
    public static Dictionary<string, SeekValueHolder> ToSeekValueHolders<T>(this List<ISortColumn<T>> sortColumns, Dictionary<string, string> cursorValues, ISeekValueConverter valueConverter)
    {
        var valueHolders = new Dictionary<string, SeekValueHolder>(cursorValues.Count, StringComparer.Ordinal);

        foreach (var field in sortColumns)
        {
            if (!cursorValues.TryGetValue(field.PropertyPath, out var cursorString))
            {
                throw new InvalidOperationException(
                    $"Cursor value for '{field.PropertyPath}' not found");
            }

            var typedValue = valueConverter.FromString(cursorString, field.KeyType);

            // ? Create holder once per field
            valueHolders[field.PropertyPath] = new SeekValueHolder { Value = typedValue };
        }

        return valueHolders;
    }

    /// <summary>
    /// Builds a <see cref="BinaryExpression"/> that represents either a
    /// strict inequality (<c>&gt;</c> / <c>&lt;</c>) or an equality (<c>==</c>)
    /// comparison between this column's property and its cursor boundary value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <paramref name="isComparison"/> is <see langword="true"/>, the
    /// generated expression is a directional inequality:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///     <c>property &gt; cursorValue</c> — forward pagination on an
    ///     ascending column, or backward pagination on a descending column.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     <c>property &lt; cursorValue</c> — forward pagination on a
    ///     descending column, or backward pagination on an ascending column.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// When <paramref name="isComparison"/> is <see langword="false"/>, the
    /// expression is always <c>property == cursorValue</c>, used to anchor
    /// tie-breaking equality predicates in OR-logic / UNION ALL strategies.
    /// </para>
    /// <para>
    /// The cursor value is read from a shared <see cref="SeekValueHolder"/>
    /// instance to ensure the same constant object is reused across all
    /// expression trees in a single page request.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="sortColumn">The sort column for which to build the predicate.</param>
    /// <param name="parameter">
    /// The <see cref="ParameterExpression"/> representing the entity in the
    /// enclosing lambda (e.g. <c>e</c> in <c>e =&gt; …</c>).
    /// </param>
    /// <param name="direction">
    /// The current seek direction; controls which inequality operator is chosen
    /// when <paramref name="isComparison"/> is <see langword="true"/>.
    /// </param>
    /// <param name="isComparison">
    /// <see langword="true"/> to produce a strict inequality;
    /// <see langword="false"/> to produce an equality check.
    /// </param>
    /// <param name="sharedValueHolders">
    /// The pre-built value holders for all sort columns in the current request,
    /// as returned by <see cref="ToSeekValueHolders{T}"/>.
    /// </param>
    /// <returns>
    /// A <see cref="BinaryExpression"/> ready to be composed into a larger
    /// filter predicate.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="sharedValueHolders"/> does not contain an
    /// entry for <see cref="ISortColumn{T}.PropertyPath"/>.
    /// </exception>
    public static BinaryExpression CreateCondition<T>(this ISortColumn<T> sortColumn, ParameterExpression parameter, SeekDirection direction, bool isComparison, Dictionary<string, SeekValueHolder> sharedValueHolders)
    {
        if (!sharedValueHolders.TryGetValue(sortColumn.PropertyPath, out var valueHolder))
            throw new InvalidOperationException($"Cursor value for '{sortColumn.PropertyPath}' not found");
        var selector = sortColumn.KeySelector;
        var visitor = new ParameterReplaceVisitor(selector.Parameters[0], parameter);
        var propertyExp = visitor.Visit(selector.Body);


        var holderConstant = Expression.Constant(valueHolder);
        var valueProperty = Expression.Property(
            holderConstant,
            nameof(SeekValueHolder.Value));
        var constantExp = Expression.Convert(valueProperty, sortColumn.KeyType);

        if (isComparison)
        {
            bool isForward = direction == SeekDirection.Next;
            bool useGreaterThan = isForward != sortColumn.IsDescending;

            return useGreaterThan
                ? Expression.GreaterThan(propertyExp, constantExp)
                : Expression.LessThan(propertyExp, constantExp);
        }
        else
        {
            return Expression.Equal(propertyExp, constantExp);
        }
    }
}
