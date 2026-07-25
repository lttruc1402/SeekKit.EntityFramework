namespace SeekKit.EntityFramework.Core;

/// <summary>
/// Main entry point for SeekKit cursor pagination. Inject this service to paginate
/// any <see cref="IQueryable{T}"/> with a stable, high-performance keyset strategy.
/// </summary>
public interface ISeekService
{
    /// <summary>
    /// Creates a fluent <see cref="ISeekBuilder{T}"/> for the given query using the
    /// globally configured options.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The base EF Core query.</param>
    ISeekBuilder<T> CreateBuilder<T>(IQueryable<T> query);

    /// <summary>
    /// Creates a fluent <see cref="ISeekBuilder{T}"/> with per-request option overrides.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The base EF Core query.</param>
    /// <param name="configure">A delegate to override global <see cref="SeekKitOptions"/>.</param>
    ISeekBuilder<T> CreateBuilder<T>(IQueryable<T> query, Action<SeekKitOptions> configure);

    /// <summary>
    /// Paginates the query in a single call using the globally configured options.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The base EF Core query.</param>
    /// <param name="request">The pagination request from the client.</param>
    /// <param name="configure">A delegate to configure ordering and strategy on the builder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<SeekResult<T>> SeekAsync<T>(
        IQueryable<T> query,
        SeekRequest request,
        Action<ISeekBuilder<T>> configure,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginates the query in a single call with per-request option overrides.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The base EF Core query.</param>
    /// <param name="request">The pagination request from the client.</param>
    /// <param name="configure">A delegate to configure ordering and strategy on the builder.</param>
    /// <param name="configureOption">A delegate to override global <see cref="SeekKitOptions"/> for this request only.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<SeekResult<T>> SeekAsync<T>(
        IQueryable<T> query,
        SeekRequest request,
        Action<ISeekBuilder<T>> configure,
        Action<SeekKitOptions> configureOption,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginates the query in a single call, applying a projection that defers
    /// the join/transform until after ordering, keyset filtering, and the
    /// look-ahead limit — see <see cref="ISeekBuilder{T}.Select{TResult}"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="query">The base EF Core query.</param>
    /// <param name="request">The pagination request from the client.</param>
    /// <param name="transformer">Projects the ordered, filtered, limited query to <typeparamref name="TResult"/>.</param>
    /// <param name="configure">A delegate to configure ordering and strategy on the builder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<SeekResult<TResult>> SeekAsync<T, TResult>(
        IQueryable<T> query,
        SeekRequest request,
        Func<IQueryable<T>, IQueryable<TResult>> transformer,
        Action<ISeekBuilder<T>> configure,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginates the query in a single call with per-request option overrides,
    /// applying a projection — see <see cref="ISeekBuilder{T}.Select{TResult}"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="query">The base EF Core query.</param>
    /// <param name="request">The pagination request from the client.</param>
    /// <param name="transformer">Projects the ordered, filtered, limited query to <typeparamref name="TResult"/>.</param>
    /// <param name="configure">A delegate to configure ordering and strategy on the builder.</param>
    /// <param name="configureOption">A delegate to override global <see cref="SeekKitOptions"/> for this request only.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<SeekResult<TResult>> SeekAsync<T, TResult>(
        IQueryable<T> query,
        SeekRequest request,
        Func<IQueryable<T>, IQueryable<TResult>> transformer,
        Action<ISeekBuilder<T>> configure,
        Action<SeekKitOptions> configureOption,
        CancellationToken cancellationToken = default);
}


internal sealed class SeekService : ISeekService
{
    private readonly ISeekFactory _seekFactory;

    public SeekService(ISeekFactory seekFactory)
    {
        _seekFactory = seekFactory;
    }


    public ISeekBuilder<T> CreateBuilder<T>(IQueryable<T> query)
    {
        return _seekFactory.CreateBuilder(query);
    }

    public ISeekBuilder<T> CreateBuilder<T>(IQueryable<T> query, Action<SeekKitOptions> configure)
    {
        return _seekFactory.CreateBuilder(query, configure);
    }

    public ValueTask<SeekResult<T>> SeekAsync<T>(IQueryable<T> query, SeekRequest request, Action<ISeekBuilder<T>> configure, CancellationToken cancellationToken = default)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (configure == null) throw new ArgumentNullException(nameof(configure));


        var buidler = _seekFactory.CreateBuilder(query);
        configure(buidler);
        return buidler.WithRequest(request).ToSeekResultAsync(cancellationToken);
    }

    public ValueTask<SeekResult<T>> SeekAsync<T>(
           IQueryable<T> query,
           SeekRequest request,
           Action<ISeekBuilder<T>> configure,
           Action<SeekKitOptions> configureOption,
           CancellationToken cancellationToken = default)
    {
        var buidler = _seekFactory.CreateBuilder(query, configureOption);
        configure(buidler);
        return buidler.WithRequest(request).ToSeekResultAsync(cancellationToken);
    }

    public ValueTask<SeekResult<TResult>> SeekAsync<T, TResult>(
        IQueryable<T> query,
        SeekRequest request,
        Func<IQueryable<T>, IQueryable<TResult>> transformer,
        Action<ISeekBuilder<T>> configure,
        CancellationToken cancellationToken = default)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (transformer == null) throw new ArgumentNullException(nameof(transformer));
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        var builder = _seekFactory.CreateBuilder(query);
        configure(builder);
        return builder.WithRequest(request).Select(transformer).ToSeekResultAsync(cancellationToken);
    }

    public ValueTask<SeekResult<TResult>> SeekAsync<T, TResult>(
        IQueryable<T> query,
        SeekRequest request,
        Func<IQueryable<T>, IQueryable<TResult>> transformer,
        Action<ISeekBuilder<T>> configure,
        Action<SeekKitOptions> configureOption,
        CancellationToken cancellationToken = default)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (transformer == null) throw new ArgumentNullException(nameof(transformer));
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        var builder = _seekFactory.CreateBuilder(query, configureOption);
        configure(builder);
        return builder.WithRequest(request).Select(transformer).ToSeekResultAsync(cancellationToken);
    }
}
