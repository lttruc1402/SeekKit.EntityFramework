namespace SeekKit.Core.Helpers;

/// <summary>
/// Statically works out which <typeparamref name="TResult"/> property ends up holding a
/// sort column's value after <c>Select()</c> projects <typeparamref name="T"/> away, so
/// the cursor can be read back from the projected page without the caller naming the
/// property.
/// <para>
/// It follows the value through the composed query the way a reader would: the sort key
/// starts at a known location on the source element (the element itself for an identity
/// selector <c>x =&gt; x</c>, or a member path such as <c>CreatedAt</c>), and each operator
/// either keeps that location (<c>Where</c>, <c>OrderBy</c>, <c>Take</c>, ...), moves it
/// (<c>Select</c>, a join's result selector), or adds an equivalent one (an inner join's
/// key equality: <c>ids.Join(orders, id =&gt; id, o =&gt; o.Id, ...)</c> makes
/// <c>o.Id</c> hold the same value). Chains of any length are supported — e.g.
/// <c>ids.Join(...).Join(...).LeftJoin(...).Select(x =&gt; new Dto { Id = x.o.Id })</c>.
/// </para>
/// <para>
/// Works without touching the database: the transformer is invoked once against an empty
/// in-memory <see cref="IQueryable{T}"/> purely to obtain the composed
/// <see cref="Expression"/> tree — <c>IQueryable</c> composition is lazy, so no query
/// executes. The probe is shared by every sort column of the builder.
/// </para>
/// <para>
/// It never guesses. A value that is computed (<c>Id = o.Id + 1</c>), comes from the
/// nullable side of an outer join, or passes through an operator this class doesn't
/// understand is reported as untraceable (<see langword="null"/>), and the caller falls
/// back to requiring an explicit property name.
/// </para>
/// </summary>
internal sealed class SortKeyResultPathTracer<T, TResult>
{
    private readonly Expression? _composed;
    private readonly Expression? _root;
    private readonly object? _probe;

    public SortKeyResultPathTracer(Func<IQueryable<T>, IQueryable<TResult>> transformer)
    {
        try
        {
            IQueryable<T> probe = Enumerable.Empty<T>().AsQueryable();
            _probe    = probe;
            _root     = probe.Expression;
            _composed = transformer(probe).Expression;
        }
        catch
        {
            // A transformer that can't be composed against an in-memory probe (e.g. it
            // needs provider-specific state up front) simply can't be traced.
            _composed = null;
        }
    }

    /// <summary>
    /// Returns the dot-separated property path on <typeparamref name="TResult"/> that holds
    /// the value selected by <paramref name="keySelector"/>,
    /// <see cref="SortKeyPathTracing.Self"/> when the projected item itself is that value,
    /// or <see langword="null"/> when it can't be traced.
    /// </summary>
    public string? TryTrace(LambdaExpression keySelector)
    {
        if (_composed is null || keySelector.Parameters.Count != 1)
            return null;

        if (!SortKeyPathTracing.TryGetMemberChain(keySelector.Body, out var parameter, out var seed)
            || parameter != keySelector.Parameters[0])
            return null;

        var paths = new SortKeyPathTracing.Walker(_root!, _probe!, seed).Resolve(_composed);
        return SortKeyPathTracing.PickReadablePath(typeof(TResult), paths);
    }
}

/// <summary>
/// The non-generic machinery behind <see cref="SortKeyResultPathTracer{T, TResult}"/>.
/// A "path" is a dot-separated member chain relative to a sequence element; the empty
/// path (<see cref="Self"/>) means the element itself is the sort value.
/// </summary>
internal static class SortKeyPathTracing
{
    /// <summary>The path of a value that is the element itself rather than one of its members.</summary>
    internal const string Self = "";

    // Operators whose output is only understood in the specific shapes handled by
    // Walker.Resolve. In any other shape (e.g. the indexed Select overload) they must not
    // fall through to the "same element type means same value" rule below.
    private static readonly HashSet<string> ShapeChangingOperators = new(StringComparer.Ordinal)
    {
        "Select", "SelectMany", "Join", "LeftJoin", "RightJoin", "GroupJoin",
        "GroupBy", "Zip", "Cast", "OfType", "Aggregate",
    };

    internal sealed class Walker(Expression root, object probe, string seed)
    {
        /// <summary>
        /// Returns every path on the element type of <paramref name="expression"/> known
        /// to hold the sort value. Empty when the value is lost or unknown.
        /// </summary>
        public HashSet<string> Resolve(Expression expression)
        {
            if (ReferenceEquals(expression, root)
                || (expression is ConstantExpression constant && ReferenceEquals(constant.Value, probe)))
                return NewSet(seed);

            if (expression is not MethodCallExpression { Object: null } call || call.Arguments.Count == 0)
                return NewSet();

            var arguments = call.Arguments;

            switch (call.Method.Name)
            {
                case "Select"
                    when arguments.Count == 2 && UnwrapLambda(arguments[1]) is { Parameters.Count: 1 } selector:
                    return PathsWithin(selector.Body, Bind(selector.Parameters[0], Resolve(arguments[0])));

                case "Join" or "LeftJoin" or "RightJoin"
                    when arguments.Count >= 5
                      && UnwrapLambda(arguments[2]) is { Parameters.Count: 1 } outerKey
                      && UnwrapLambda(arguments[3]) is { Parameters.Count: 1 } innerKey
                      && UnwrapLambda(arguments[4]) is { Parameters.Count: 2 } joinResult:
                    return ResolveJoin(call, outerKey, innerKey, joinResult);

                // (outer, group) => ... — only the outer element is a single row here; the
                // grouped inner side is a collection and is handled by a following
                // SelectMany, where it is nullable anyway (DefaultIfEmpty).
                case "GroupJoin"
                    when arguments.Count >= 5 && UnwrapLambda(arguments[4]) is { Parameters.Count: 2 } groupResult:
                    return PathsWithin(groupResult.Body, Bind(groupResult.Parameters[0], Resolve(arguments[0])));

                // source.SelectMany(x => collection, (x, item) => ...) — the classic
                // left-join tail. The source element keeps its paths; the collection item
                // isn't traced.
                case "SelectMany"
                    when arguments.Count == 3 && UnwrapLambda(arguments[2]) is { Parameters.Count: 2 } manyResult:
                    return PathsWithin(manyResult.Body, Bind(manyResult.Parameters[0], Resolve(arguments[0])));
            }

            if (ShapeChangingOperators.Contains(call.Method.Name))
                return NewSet();

            // Everything else that maps a sequence to a sequence of the same element type
            // (Where, OrderBy, Take, Distinct, AsNoTracking, Include, TagWith, custom
            // filter extensions, ...) filters or reorders rows without rewriting them.
            return SameElementType(call.Type, arguments[0].Type)
                ? Resolve(arguments[0])
                : NewSet();
        }

        private HashSet<string> ResolveJoin(
            MethodCallExpression call,
            LambdaExpression outerKey,
            LambdaExpression innerKey,
            LambdaExpression resultSelector)
        {
            var outer = Resolve(call.Arguments[0]);
            var inner = Resolve(call.Arguments[1]);

            switch (call.Method.Name)
            {
                // Inner join: every surviving row satisfies outerKey == innerKey, so a key
                // part known to hold the sort value on one side makes the matching part
                // on the other side hold it too.
                case "Join":
                    PropagateKeyEquality(outerKey, outer, innerKey, inner);
                    break;

                // Outer joins: the optional side is null for unmatched rows, so nothing
                // read from it is a reliable cursor value.
                case "LeftJoin":
                    inner = NewSet();
                    break;

                case "RightJoin":
                    outer = NewSet();
                    break;
            }

            var bindings = Bind(resultSelector.Parameters[0], outer);
            bindings[resultSelector.Parameters[1]] = inner;
            return PathsWithin(resultSelector.Body, bindings);
        }

        private static void PropagateKeyEquality(
            LambdaExpression outerKey, HashSet<string> outer,
            LambdaExpression innerKey, HashSet<string> inner)
        {
            var outerParts = KeyParts(outerKey.Body);
            var innerParts = KeyParts(innerKey.Body);

            if (outerParts.Count != innerParts.Count)
                return;

            var outerBindings = Bind(outerKey.Parameters[0], outer);
            var innerBindings = Bind(innerKey.Parameters[0], inner);

            List<string>? addToInner = null;
            List<string>? addToOuter = null;

            for (int i = 0; i < outerParts.Count; i++)
            {
                bool outerHoldsValue = PathsWithin(outerParts[i], outerBindings).Contains(Self);
                bool innerHoldsValue = PathsWithin(innerParts[i], innerBindings).Contains(Self);

                if (outerHoldsValue
                    && TryGetMemberChain(innerParts[i], out var innerParameter, out var innerChain)
                    && innerParameter == innerKey.Parameters[0])
                    (addToInner ??= []).Add(innerChain);

                if (innerHoldsValue
                    && TryGetMemberChain(outerParts[i], out var outerParameter, out var outerChain)
                    && outerParameter == outerKey.Parameters[0])
                    (addToOuter ??= []).Add(outerChain);
            }

            if (addToInner is not null) inner.UnionWith(addToInner);
            if (addToOuter is not null) outer.UnionWith(addToOuter);
        }

        // A composite key (new { a.X, a.Y }) compares member by member; a plain key is a
        // single part.
        private static IReadOnlyList<Expression> KeyParts(Expression keyBody)
        {
            keyBody = StripConversions(keyBody);
            return keyBody is NewExpression { Members: not null } composite
                ? composite.Arguments
                : [keyBody];
        }

        private static Dictionary<ParameterExpression, HashSet<string>> Bind(ParameterExpression parameter, HashSet<string> paths)
            => new() { [parameter] = paths };
    }

    /// <summary>
    /// Returns every path inside the value of <paramref name="expression"/> that holds the
    /// sort value, given the known paths of the lambda parameters it reads from.
    /// </summary>
    private static HashSet<string> PathsWithin(Expression expression, Dictionary<ParameterExpression, HashSet<string>> bindings)
    {
        var found = NewSet();
        Collect(expression, bindings, prefix: Self, found);
        return found;
    }

    private static void Collect(
        Expression expression,
        Dictionary<ParameterExpression, HashSet<string>> bindings,
        string prefix,
        HashSet<string> found)
    {
        expression = StripConversions(expression);

        switch (expression)
        {
            // x, x.A, x.A.B — a (possibly empty) member chain on a lambda parameter. Every
            // known path that runs through this chain continues inside its value.
            case ParameterExpression or MemberExpression:
                if (TryGetMemberChain(expression, out var parameter, out var chain)
                    && bindings.TryGetValue(parameter, out var known))
                {
                    foreach (var path in known)
                    {
                        if (TryStripPrefix(path, chain, out var rest))
                            found.Add(Combine(prefix, rest));
                    }
                }
                break;

            // new Dto(...) { A = ..., B = { C = ... } }
            case MemberInitExpression init:
                Collect(init.NewExpression, bindings, prefix, found);
                CollectBindings(init.Bindings, bindings, prefix, found);
                break;

            // new { A = ..., B = ... }, or a positional record / tuple constructor.
            case NewExpression created:
                for (int i = 0; i < created.Arguments.Count; i++)
                {
                    var member = created.Members is not null
                        ? MemberName(created.Members[i])
                        : PropertyInitializedByConstructorParameter(created, i);

                    if (member is not null)
                        Collect(created.Arguments[i], bindings, Combine(prefix, member), found);
                }
                break;
        }
    }

    private static void CollectBindings(
        IEnumerable<MemberBinding> memberBindings,
        Dictionary<ParameterExpression, HashSet<string>> bindings,
        string prefix,
        HashSet<string> found)
    {
        foreach (var binding in memberBindings)
        {
            switch (binding)
            {
                case MemberAssignment assignment:
                    Collect(assignment.Expression, bindings, Combine(prefix, assignment.Member.Name), found);
                    break;

                case MemberMemberBinding nested:
                    CollectBindings(nested.Bindings, bindings, Combine(prefix, nested.Member.Name), found);
                    break;
            }
        }
    }

    /// <summary>
    /// Reads <paramref name="expression"/> as a member chain on a lambda parameter
    /// (<c>x</c> → empty chain, <c>x.A.B</c> → <c>"A.B"</c>), looking through value-preserving
    /// conversions.
    /// </summary>
    internal static bool TryGetMemberChain(Expression expression, out ParameterExpression parameter, out string chain)
    {
        List<string>? names = null;

        while (true)
        {
            expression = StripConversions(expression);

            if (expression is MemberExpression { Expression: not null } member)
            {
                (names ??= []).Add(member.Member.Name);
                expression = member.Expression;
                continue;
            }

            if (expression is ParameterExpression root)
            {
                parameter = root;
                if (names is null)
                {
                    chain = Self;
                }
                else
                {
                    names.Reverse();
                    chain = string.Join(".", names);
                }
                return true;
            }

            parameter = null!;
            chain = Self;
            return false;
        }
    }

    /// <summary>
    /// Picks the shortest traced path that can actually be read off
    /// <paramref name="resultType"/> through public instance properties.
    /// </summary>
    internal static string? PickReadablePath(Type resultType, HashSet<string> paths)
    {
        if (paths.Contains(Self))
            return Self;

        return paths
            .OrderBy(static path => path.Count(static c => c == '.'))
            .ThenBy(static path => path, StringComparer.Ordinal)
            .FirstOrDefault(path => ResultKeyAccessor.CanRead(resultType, path));
    }

    // Convert/TypeAs nodes the compiler inserts (int → int?, boxing, interface casts) don't
    // change which value is being read. A user-defined conversion operator does, so stop there.
    private static Expression StripConversions(Expression expression)
    {
        while (expression is UnaryExpression
               {
                   NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs,
                   Method: null,
               } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static LambdaExpression? UnwrapLambda(Expression expression) => expression switch
    {
        UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression quoted } => quoted,
        LambdaExpression lambda => lambda,
        _ => null,
    };

    private static bool TryStripPrefix(string path, string chain, out string rest)
    {
        if (chain.Length == 0)
        {
            rest = path;
            return true;
        }

        if (path.Length == chain.Length && string.Equals(path, chain, StringComparison.Ordinal))
        {
            rest = Self;
            return true;
        }

        if (path.Length > chain.Length
            && path[chain.Length] == '.'
            && path.StartsWith(chain, StringComparison.Ordinal))
        {
            rest = path.Substring(chain.Length + 1);
            return true;
        }

        rest = Self;
        return false;
    }

    private static string Combine(string prefix, string rest)
    {
        if (prefix.Length == 0) return rest;
        if (rest.Length == 0) return prefix;
        return prefix + "." + rest;
    }

    // NewExpression.Members holds properties for anonymous types, but may hold the getter
    // methods instead depending on how the tree was built.
    private static string MemberName(MemberInfo member)
        => member is MethodInfo method && method.Name.StartsWith("get_", StringComparison.Ordinal)
            ? method.Name.Substring(4)
            : member.Name;

    // new Dto(o.Id, o.Name) carries no member list, but a positional record (or Tuple<>)
    // exposes each constructor parameter as a same-named, same-typed property.
    private static string? PropertyInitializedByConstructorParameter(NewExpression created, int index)
    {
        var parameters = created.Constructor?.GetParameters();
        if (parameters is null || index >= parameters.Length || parameters[index].Name is not { } name)
            return null;

        var property = created.Type.GetProperty(
            name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        return property is { CanRead: true } && property.PropertyType == parameters[index].ParameterType
            ? property.Name
            : null;
    }

    private static bool SameElementType(Type left, Type right)
    {
        var leftElement = SequenceElementType(left);
        return leftElement is not null && leftElement == SequenceElementType(right);
    }

    private static Type? SequenceElementType(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return type.GetGenericArguments()[0];

        foreach (var contract in type.GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return contract.GetGenericArguments()[0];
        }

        return null;
    }

    private static HashSet<string> NewSet() => new(StringComparer.Ordinal);

    private static HashSet<string> NewSet(string path) => new(StringComparer.Ordinal) { path };
}
