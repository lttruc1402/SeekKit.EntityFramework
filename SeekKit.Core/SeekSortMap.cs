namespace SeekKit.Core;

/// <summary>
/// One resolved entry of a client-supplied sort: a whitelisted field and its direction.
/// </summary>
public readonly struct SeekSortField
{
    /// <summary>Creates the entry.</summary>
    public SeekSortField(string name, bool descending)
    {
        Name = name;
        Descending = descending;
    }

    /// <summary>The field name, spelled the way it was registered on the <see cref="SeekSortMap{T}"/>.</summary>
    public string Name { get; }

    /// <summary><see langword="true"/> for descending.</summary>
    public bool Descending { get; }

    /// <summary>Formats the entry the way it is written in a sort string: <c>name:asc</c> or <c>name:desc</c>.</summary>
    public override string ToString() => Descending ? Name + ":desc" : Name + ":asc";
}

/// <summary>
/// Thrown when a client-supplied sort string can't be applied: unknown field, bad
/// syntax, a field listed twice, or too many fields. Always the caller's mistake —
/// answer it with <c>400 Bad Request</c>.
/// </summary>
public sealed class SeekSortException : ArgumentException
{
    /// <summary>Creates the exception.</summary>
    public SeekSortException(string message, string? field = null)
        : base(message)
    {
        Field = field;
    }

    /// <summary>The offending field as the client wrote it, when the problem is about one field.</summary>
    public string? Field { get; }
}

/// <summary>
/// Receives the sort columns a <see cref="SeekSortMap{T}"/> resolves, in priority order.
/// Each provider package adapts its own builder to this — use the builder's
/// <c>ApplySort(sortMap, sort)</c> extension rather than implementing it yourself.
/// </summary>
/// <typeparam name="T">The entity/document type being sorted.</typeparam>
public interface ISeekSortTarget<T>
{
    /// <summary>Adds the next sort column.</summary>
    void OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, bool descending, string? resultPropertyName);
}

/// <summary>
/// A whitelist that turns a client-supplied sort string (<c>?sort=price:desc,name:asc</c>) into
/// sort columns. Only fields registered here can be sorted by, so a client can never make
/// the database sort on an arbitrary (unindexed, private) column.
/// </summary>
/// <remarks>
/// <para>Build one instance per endpoint and reuse it — it is immutable once in use and thread-safe to read.</para>
/// <para>
/// Sort string syntax: comma-separated entries, each written <c>field:asc</c> or
/// <c>field:desc</c> — for example <c>price:desc,name:asc</c>. The direction may be left
/// out, in which case it is ascending. That is the only spelling accepted: <c>-price</c>
/// and <c>price desc</c> are rejected with a <see cref="SeekSortException"/> that says
/// what to write instead. Field names and directions are matched case-insensitively.
/// </para>
/// <example>
/// <code>
/// static readonly SeekSortMap&lt;Product&gt; ProductSorts = new SeekSortMap&lt;Product&gt;()
///     .Add("createdAt", p =&gt; p.CreatedAt)
///     .Add("price",     p =&gt; p.Price)
///     .Add("name",      p =&gt; p.Name)
///     .Add("id",        p =&gt; p.Id)
///     .Default("createdAt:desc")
///     .TieBreaker("id");
///
/// var page = await seek.SeekAsync(db.Products, request, b =&gt; b.ApplySort(ProductSorts, sort));
/// </code>
/// </example>
/// </remarks>
/// <typeparam name="T">The entity/document type being sorted.</typeparam>
public sealed class SeekSortMap<T>
{
    private static readonly char[] FieldSeparator = [','];

    private readonly Dictionary<string, Entry> _fields = new(StringComparer.OrdinalIgnoreCase);
    private string? _defaultSort;
    private string? _tieBreaker;
    private bool? _tieBreakerDescending;
    private int _maxFields = 5;

    /// <summary>The registered field names, in registration order.</summary>
    public IReadOnlyCollection<string> Fields => _fields.Values.Select(e => e.Name).ToArray();

    /// <summary>Whitelists a field clients may sort by.</summary>
    /// <param name="name">The name clients use in the sort string.</param>
    /// <param name="keySelector">The column (or translatable expression) it sorts on.</param>
    /// <param name="resultPropertyName">
    /// Passed through to the builder's <c>OrderBy</c> — only needed in the rare cases a
    /// <c>Select()</c> projection can't trace the value by itself.
    /// </param>
    public SeekSortMap<T> Add<TKey>(string name, Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null)
    {
        if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A sort field needs a name.", nameof(name));

        name = name.Trim();
        if (name.IndexOfAny([',', ':', ' ']) >= 0 || name[0] is '-' or '+')
            throw new ArgumentException($"Sort field name '{name}' may not contain ',', ':' or spaces, or start with '-' or '+'.", nameof(name));
        if (_fields.ContainsKey(name))
            throw new ArgumentException($"Sort field '{name}' is already registered.", nameof(name));

        _fields.Add(name, new Entry<TKey>(name, keySelector, resultPropertyName));
        return this;
    }

    /// <summary>
    /// The sort to use when the client sends none, in the same syntax clients use
    /// (e.g. <c>"createdAt:desc"</c>).
    /// </summary>
    public SeekSortMap<T> Default(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            throw new ArgumentException("The default sort may not be empty.", nameof(sort));

        _defaultSort = sort;
        return this;
    }

    /// <summary>
    /// Names the unique field (usually the primary key) appended after whatever the client
    /// asked for, unless the client already sorts by it. Keyset pagination needs a unique
    /// last column — without one, rows that tie on every sort column can be skipped or
    /// repeated across pages.
    /// </summary>
    /// <param name="field">A field registered with <see cref="Add"/>.</param>
    /// <param name="descending">
    /// The tie-breaker's direction. <see langword="null"/> (the default) follows the
    /// direction of the first sort field, which keeps all-ascending / all-descending sorts
    /// eligible for a row-value comparison.
    /// </param>
    public SeekSortMap<T> TieBreaker(string field, bool? descending = null)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("The tie-breaker needs a field name.", nameof(field));

        _tieBreaker = field.Trim();
        _tieBreakerDescending = descending;
        return this;
    }

    /// <summary>
    /// Caps how many fields a client may sort by at once (default <c>5</c>). The
    /// tie-breaker doesn't count.
    /// </summary>
    public SeekSortMap<T> LimitTo(int maxFields)
    {
        if (maxFields < 1) throw new ArgumentOutOfRangeException(nameof(maxFields));
        _maxFields = maxFields;
        return this;
    }

    /// <summary>
    /// Parses and validates <paramref name="sort"/> and returns the columns that will be
    /// sorted by, tie-breaker included — handy for echoing the effective sort back to the
    /// client.
    /// </summary>
    /// <exception cref="SeekSortException">The sort string is invalid.</exception>
    public IReadOnlyList<SeekSortField> Resolve(string? sort)
        => ResolveEntries(sort).Select(x => new SeekSortField(x.Entry.Name, x.Descending)).ToArray();

    /// <summary>
    /// Parses and validates <paramref name="sort"/> and adds the resulting columns to
    /// <paramref name="target"/> in priority order.
    /// </summary>
    /// <exception cref="SeekSortException">The sort string is invalid.</exception>
    public void Apply(string? sort, ISeekSortTarget<T> target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));

        foreach (var (entry, descending) in ResolveEntries(sort))
            entry.Apply(target, descending);
    }

    private List<(Entry Entry, bool Descending)> ResolveEntries(string? sort)
    {
        bool fromClient = !string.IsNullOrWhiteSpace(sort);
        var text = fromClient ? sort! : _defaultSort;

        var resolved = new List<(Entry Entry, bool Descending)>();

        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (var rawPart in text!.Split(FieldSeparator))
            {
                var part = rawPart.Trim();
                if (part.Length == 0)
                    continue;

                var (name, descending) = ParsePart(part);

                if (!_fields.TryGetValue(name, out var entry))
                    throw new SeekSortException(
                        $"Cannot sort by '{name}'. Sortable fields: {string.Join(", ", Fields)}.", name);

                if (resolved.Exists(x => ReferenceEquals(x.Entry, entry)))
                    throw new SeekSortException($"Sort field '{entry.Name}' is listed more than once.", name);

                resolved.Add((entry, descending));
            }

            if (resolved.Count > _maxFields)
                throw new SeekSortException($"At most {_maxFields} sort fields are allowed, {resolved.Count} were given.");
        }

        if (_tieBreaker is not null)
        {
            if (!_fields.TryGetValue(_tieBreaker, out var tieBreaker))
                throw new InvalidOperationException(
                    $"The tie-breaker '{_tieBreaker}' is not a registered sort field. Register it with Add() first.");

            if (!resolved.Exists(x => ReferenceEquals(x.Entry, tieBreaker)))
            {
                bool descending = _tieBreakerDescending ?? (resolved.Count > 0 && resolved[0].Descending);
                resolved.Add((tieBreaker, descending));
            }
        }

        if (resolved.Count == 0)
        {
            if (fromClient)
                throw new SeekSortException("The sort is empty.");

            throw new InvalidOperationException(
                "No sort was requested and the SeekSortMap has neither a Default() sort nor a TieBreaker().");
        }

        return resolved;
    }

    // One entry is "field", "field:asc" or "field:desc" — nothing else. A single explicit
    // spelling keeps URLs readable and leaves no room for two clients to mean different
    // things by the same string.
    private static (string Name, bool Descending) ParsePart(string part)
    {
        const string Hint = " Write each sort entry as 'field:asc' or 'field:desc'.";

        string name = part;
        bool descending = false;

        int colon = part.IndexOf(':');
        if (colon >= 0)
        {
            name = part.Substring(0, colon).Trim();
            var direction = part.Substring(colon + 1).Trim();

            if (direction.Equals("desc", StringComparison.OrdinalIgnoreCase))
                descending = true;
            else if (!direction.Equals("asc", StringComparison.OrdinalIgnoreCase))
                throw new SeekSortException($"'{direction}' is not a sort direction." + Hint, part);
        }

        if (name.Length == 0)
            throw new SeekSortException("A sort entry is missing its field name." + Hint, part);

        // "-price", "+price" and "price desc" are common in other APIs — say what to write instead.
        if (name[0] is '-' or '+' || name.IndexOf(' ') >= 0)
            throw new SeekSortException($"'{part}' is not a valid sort entry." + Hint, part);

        return (name, descending);
    }

    private abstract class Entry
    {
        protected Entry(string name) => Name = name;

        public string Name { get; }

        public abstract void Apply(ISeekSortTarget<T> target, bool descending);
    }

    private sealed class Entry<TKey> : Entry
    {
        private readonly Expression<Func<T, TKey>> _keySelector;
        private readonly string? _resultPropertyName;

        public Entry(string name, Expression<Func<T, TKey>> keySelector, string? resultPropertyName)
            : base(name)
        {
            _keySelector = keySelector;
            _resultPropertyName = resultPropertyName;
        }

        public override void Apply(ISeekSortTarget<T> target, bool descending)
            => target.OrderBy(_keySelector, descending, _resultPropertyName);
    }
}
