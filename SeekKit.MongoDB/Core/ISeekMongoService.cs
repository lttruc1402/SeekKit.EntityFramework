
namespace SeekKit.MongoDB.Core;

/// <summary>
/// Main entry point for SeekKit cursor pagination over MongoDB. Inject this
/// service to paginate any <see cref="IMongoCollection{T}"/> (or an
/// <see cref="IQueryable{T}"/> produced by the driver's LINQ provider) with a
/// stable, high-performance keyset strategy.
/// </summary>
public interface ISeekMongoService
{
    /// <summary>
    /// Creates a fluent <see cref="ISeekMongoBuilder{T}"/> for the given collection.
    /// </summary>
    ISeekMongoQueryableBuilder<T> CreateBuilder<T>(IMongoCollection<T> collection);

    /// <summary>
    /// Creates a fluent <see cref="ISeekMongoBuilder{T}"/> for a queryable —
    /// typically <c>collection.AsQueryable().Where(...)</c> with filters already applied.
    /// </summary>
    ISeekMongoQueryableBuilder<T> CreateBuilder<T>(IQueryable<T> query);

    /// <summary>
    /// Creates a fluent <see cref="ISeekMongoBuilder{T}"/> for an aggregation
    /// pipeline — e.g. <c>collection.Aggregate().UnionWith(other)</c> or a
    /// pipeline containing <c>$lookup</c>/<c>$match</c> stages. SeekKit appends a
    /// keyset <c>$match</c>, a <c>$sort</c>, and a <c>$limit</c>.
    /// </summary>
    /// <remarks>
    /// Keyset pagination requires the sort columns to be present as real fields on
    /// the pipeline output (and, ideally, indexed). Sorting on values produced by
    /// <c>$group</c> or computed <c>$project</c> stages will work but cannot use an
    /// index. The tie-breaker column must be globally unique across the whole
    /// pipeline output.
    /// </remarks>
    ISeekMongoAggregateBuilder<T> CreateBuilder<T>(IAggregateFluent<T> aggregate);

    /// <summary>
    /// Creates a fluent <see cref="ISeekMongoBuilder{T}"/> for a find query —
    /// e.g. <c>collection.Find(bsonFilter)</c> with a BSON
    /// <see cref="FilterDefinition{T}"/> (text search, geo, or any filter the
    /// LINQ provider can't express). SeekKit AND-s the keyset predicate into the
    /// query filter and appends a sort and a limit.
    /// </summary>
    ISeekMongoFindBuilder<T> CreateBuilder<T>(IFindFluent<T, T> find);

    /// <summary>
    /// Paginates the whole collection in a single call.
    /// </summary>
    /// <param name="collection">The collection to paginate.</param>
    /// <param name="request">The pagination request from the client.</param>
    /// <param name="configure">A delegate to configure ordering (and optionally strategy) on the builder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<SeekResult<T>> SeekAsync<T>(
        IMongoCollection<T> collection,
        SeekRequest request,
        Action<ISeekMongoBuilder<T>> configure,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginates a pre-filtered queryable in a single call.
    /// </summary>
    /// <param name="query">The base query, e.g. <c>collection.AsQueryable().Where(x => x.IsActive)</c>.</param>
    /// <param name="request">The pagination request from the client.</param>
    /// <param name="configure">A delegate to configure ordering (and optionally strategy) on the builder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<SeekResult<T>> SeekAsync<T>(
        IQueryable<T> query,
        SeekRequest request,
        Action<ISeekMongoBuilder<T>> configure,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginates an aggregation pipeline in a single call. See
    /// <see cref="CreateBuilder{T}(IAggregateFluent{T})"/> for the requirements.
    /// </summary>
    /// <param name="aggregate">The aggregation pipeline, e.g. <c>collection.Aggregate().UnionWith(other)</c>.</param>
    /// <param name="request">The pagination request from the client.</param>
    /// <param name="configure">A delegate to configure ordering on the builder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<SeekResult<T>> SeekAsync<T>(
        IAggregateFluent<T> aggregate,
        SeekRequest request,
        Action<ISeekMongoBuilder<T>> configure,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginates a find query in a single call. See
    /// <see cref="CreateBuilder{T}(IFindFluent{T, T})"/> for details.
    /// </summary>
    /// <param name="find">The find query, e.g. <c>collection.Find(bsonFilter)</c>.</param>
    /// <param name="request">The pagination request from the client.</param>
    /// <param name="configure">A delegate to configure ordering on the builder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<SeekResult<T>> SeekAsync<T>(
        IFindFluent<T, T> find,
        SeekRequest request,
        Action<ISeekMongoBuilder<T>> configure,
        CancellationToken cancellationToken = default);
}


internal sealed class SeekMongoService : ISeekMongoService
{
    private readonly ISeekSerializer _serializer;
    private readonly ISeekValueConverter _valueConverter;
    private readonly IOptions<SeekKitMongoOptions> _options;

    public SeekMongoService(ISeekSerializer serializer, ISeekValueConverter valueConverter, IOptions<SeekKitMongoOptions> options)
    {
        _serializer     = serializer;
        _valueConverter = valueConverter;
        _options        = options;
    }

    public ISeekMongoQueryableBuilder<T> CreateBuilder<T>(IMongoCollection<T> collection)
    {
        if (collection is null) throw new ArgumentNullException(nameof(collection));
        return new SeekMongoBuilder<T>(collection.AsQueryable(), _serializer, _valueConverter, _options.Value);
    }

    public ISeekMongoQueryableBuilder<T> CreateBuilder<T>(IQueryable<T> query)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));
        return new SeekMongoBuilder<T>(query, _serializer, _valueConverter, _options.Value);
    }

    public ISeekMongoAggregateBuilder<T> CreateBuilder<T>(IAggregateFluent<T> aggregate)
    {
        if (aggregate is null) throw new ArgumentNullException(nameof(aggregate));
        return new SeekAggregateBuilder<T>(aggregate, _serializer, _valueConverter, _options.Value);
    }

    public ISeekMongoFindBuilder<T> CreateBuilder<T>(IFindFluent<T, T> find)
    {
        if (find is null) throw new ArgumentNullException(nameof(find));
        return new SeekFindBuilder<T>(find, _serializer, _valueConverter, _options.Value);
    }

    public ValueTask<SeekResult<T>> SeekAsync<T>(
        IMongoCollection<T> collection,
        SeekRequest request,
        Action<ISeekMongoBuilder<T>> configure,
        CancellationToken cancellationToken = default)
    {
        if (collection is null) throw new ArgumentNullException(nameof(collection));
        return SeekAsync(collection.AsQueryable(), request, configure, cancellationToken);
    }

    public ValueTask<SeekResult<T>> SeekAsync<T>(
        IQueryable<T> query,
        SeekRequest request,
        Action<ISeekMongoBuilder<T>> configure,
        CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        var builder = CreateBuilder(query).WithRequest(request);
        configure(builder);
        return builder.ToSeekResultAsync(cancellationToken);
    }

    public ValueTask<SeekResult<T>> SeekAsync<T>(
        IAggregateFluent<T> aggregate,
        SeekRequest request,
        Action<ISeekMongoBuilder<T>> configure,
        CancellationToken cancellationToken = default)
    {
        if (aggregate is null) throw new ArgumentNullException(nameof(aggregate));
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        var builder = CreateBuilder(aggregate).WithRequest(request);
        configure(builder);
        return builder.ToSeekResultAsync(cancellationToken);
    }

    public ValueTask<SeekResult<T>> SeekAsync<T>(
        IFindFluent<T, T> find,
        SeekRequest request,
        Action<ISeekMongoBuilder<T>> configure,
        CancellationToken cancellationToken = default)
    {
        if (find is null) throw new ArgumentNullException(nameof(find));
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        var builder = CreateBuilder(find).WithRequest(request);
        configure(builder);
        return builder.ToSeekResultAsync(cancellationToken);
    }
}
