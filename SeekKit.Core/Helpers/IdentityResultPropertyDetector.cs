namespace SeekKit.Core.Helpers;

/// <summary>
/// Best-effort static detection of which <c>TResult</c> property holds the same
/// value as an identity sort selector (<c>x =&gt; x</c>) after <c>Select()</c>
/// projects <c>T</c> away. Recognizes two common shapes:
/// <list type="bullet">
///   <item><description>
///   A single <c>Queryable.Join</c> whose outer key selector is the
///   identity and whose result selector assigns the matching inner key value
///   directly to a <c>TResult</c> member — e.g. <c>ids.Join(entities, x =&gt; x,
///   e =&gt; e.Id, (_, e) =&gt; new TResult { Id = e.Id, ... })</c>.
///   </description></item>
///   <item><description>
///   A single <c>Queryable.Select</c> that assigns the identity
///   parameter directly to a member — e.g. <c>ids.Select(id =&gt; new TResult
///   { Id = id, ... })</c>.
///   </description></item>
/// </list>
/// <para>
/// Works without ever touching the database: the transformer is invoked once
/// against an empty in-memory <see cref="IQueryable{T}"/> purely to obtain the
/// composed <see cref="Expression"/> tree — <c>IQueryable</c> composition is
/// lazy, so no query executes.
/// </para>
/// <para>
/// Returns <see langword="null"/> (rather than guessing) for anything outside
/// this shape — nested joins, computed member values, a rename after the join,
/// etc. — so the caller can fall back to requiring an explicit property name.
/// </para>
/// </summary>
internal static class IdentityResultPropertyDetector
{
    public static string? TryDetect<T, TResult>(Func<IQueryable<T>, IQueryable<TResult>> transformer)
    {
        IQueryable<TResult> probe;
        try
        {
            probe = transformer(Enumerable.Empty<T>().AsQueryable());
        }
        catch
        {
            return null;
        }

        return FindInExpression(probe.Expression, typeof(T));
    }

    private static string? FindInExpression(Expression? expression, Type outerType)
    {
        if (expression is not MethodCallExpression call)
            return null;

        if (call.Method.DeclaringType == typeof(Queryable) && call.Arguments.Count > 0)
        {
            string? detected = call.Method.Name switch
            {
                nameof(Queryable.Join) when call.Method.GetGenericArguments()[0] == outerType
                    && call.Arguments.Count == 5 => TryMatchJoin(call),

                nameof(Queryable.Select) when call.Method.GetGenericArguments()[0] == outerType
                    && call.Arguments.Count == 2 => TryMatchSelect(call),

                _ => null,
            };

            if (detected is not null)
                return detected;
        }

        // The Join/Select might be nested deeper (e.g. wrapped in an outer .Where()).
        foreach (var argument in call.Arguments)
        {
            if (FindInExpression(argument, outerType) is { } found)
                return found;
        }

        return null;
    }

    private static string? TryMatchJoin(MethodCallExpression joinCall)
    {
        if (UnwrapLambda(joinCall.Arguments[2]) is not { } outerKeySelector || !IsIdentity(outerKeySelector))
            return null;

        if (UnwrapLambda(joinCall.Arguments[3]) is not { } innerKeySelector)
            return null;

        if (UnwrapLambda(joinCall.Arguments[4]) is not { Parameters.Count: 2 } resultSelector)
            return null;

        var innerParam = resultSelector.Parameters[1];
        var innerKeyBody = new ParameterReplaceVisitor(innerKeySelector.Parameters[0], innerParam)
            .Visit(innerKeySelector.Body);

        return resultSelector.Body switch
        {
            MemberInitExpression memberInit => memberInit.Bindings
                .OfType<MemberAssignment>()
                .Where(b => StructurallyEqual(b.Expression, innerKeyBody))
                .Select(b => b.Member.Name)
                .FirstOrDefault(),

            NewExpression { Members: not null } newExpr => newExpr.Members
                .Where((_, i) => StructurallyEqual(newExpr.Arguments[i], innerKeyBody))
                .Select(m => m.Name)
                .FirstOrDefault(),

            _ => null,
        };
    }

    private static string? TryMatchSelect(MethodCallExpression selectCall)
    {
        if (UnwrapLambda(selectCall.Arguments[1]) is not { Parameters.Count: 1 } selector)
            return null;

        var param = selector.Parameters[0];

        return selector.Body switch
        {
            MemberInitExpression memberInit => memberInit.Bindings
                .OfType<MemberAssignment>()
                .Where(b => StructurallyEqual(b.Expression, param))
                .Select(b => b.Member.Name)
                .FirstOrDefault(),

            NewExpression { Members: not null } newExpr => newExpr.Members
                .Where((_, i) => StructurallyEqual(newExpr.Arguments[i], param))
                .Select(m => m.Name)
                .FirstOrDefault(),

            _ => null,
        };
    }

    private static LambdaExpression? UnwrapLambda(Expression expression) => expression switch
    {
        UnaryExpression { Operand: LambdaExpression lambda } => lambda,
        LambdaExpression lambda => lambda,
        _ => null,
    };

    private static bool IsIdentity(LambdaExpression lambda) =>
        lambda.Parameters.Count == 1 && lambda.Body == lambda.Parameters[0];

    private static bool StructurallyEqual(Expression a, Expression b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.NodeType != b.NodeType || a.Type != b.Type) return false;

        return (a, b) switch
        {
            (MemberExpression ma, MemberExpression mb) =>
                ma.Member == mb.Member && StructurallyEqual(ma.Expression!, mb.Expression!),
            (ParameterExpression, ParameterExpression) => ReferenceEquals(a, b),
            (ConstantExpression ca, ConstantExpression cb) => Equals(ca.Value, cb.Value),
            (UnaryExpression ua, UnaryExpression ub) => StructurallyEqual(ua.Operand, ub.Operand),
            _ => false,
        };
    }

    private sealed class ParameterReplaceVisitor(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}
