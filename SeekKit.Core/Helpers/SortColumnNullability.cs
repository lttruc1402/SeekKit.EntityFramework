namespace SeekKit.Core.Helpers;

/// <summary>
/// Decides whether a sort column can hold <c>NULL</c>, i.e. whether its keyset filter has
/// to be null-aware and whether a row-value (tuple) comparison is off the table.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>A non-nullable value type (<c>int</c>, <c>DateTime</c>, …) never is.</description></item>
///   <item><description><c>Nullable&lt;T&gt;</c> always is.</description></item>
///   <item><description>
///   A reference type (<c>string</c>, …) is, unless every member on the path is annotated
///   as non-nullable (<c>string Name</c> in a nullable-enabled project). Without
///   annotations there is nothing to go by, so the column is treated as nullable — the
///   extra <c>IS NULL</c> branch is harmless on a column that never holds one.
///   </description></item>
/// </list>
/// </remarks>
internal static class SortColumnNullability
{
#if NET6_0_OR_GREATER
    private static readonly ConcurrentDictionary<MemberInfo, bool> MemberCache = new();
#endif

    public static bool CanBeNull<T>(ISortColumn<T> column)
    {
        var type = column.KeyType;
        if (type.IsValueType)
            return Nullable.GetUnderlyingType(type) is not null;

#if NET6_0_OR_GREATER
        var body = column.KeySelector.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs } unary)
            body = unary.Operand;

        // Every hop of o => o.Customer.Name has to be non-nullable for the value to be.
        bool sawMember = false;
        while (body is MemberExpression member)
        {
            sawMember = true;
            if (MemberCache.GetOrAdd(member.Member, IsDeclaredNullable))
                return true;
            body = member.Expression!;
        }

        return !(sawMember && body is ParameterExpression);
#else
        return true;
#endif
    }

#if NET6_0_OR_GREATER
    private static bool IsDeclaredNullable(MemberInfo member)
    {
        // Value-typed hops (o.Period.Start) are nullable only as Nullable<T>.
        var memberType = member switch
        {
            PropertyInfo p => p.PropertyType,
            FieldInfo f    => f.FieldType,
            _              => null,
        };
        if (memberType is null)
            return true;
        if (memberType.IsValueType)
            return Nullable.GetUnderlyingType(memberType) is not null;

        try
        {
            var context = new NullabilityInfoContext();
            var info = member is PropertyInfo property
                ? context.Create(property)
                : context.Create((FieldInfo)member);
            return info.ReadState != NullabilityState.NotNull;
        }
        catch (Exception)
        {
            // NullabilityInfoContext can be switched off (trimmed apps) — assume nullable.
            return true;
        }
    }
#endif
}
