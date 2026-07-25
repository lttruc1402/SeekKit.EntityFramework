namespace SeekKit.EntityFramework.Helpers;

/// <summary>
/// Extension methods on <see cref="IQueryable{T}"/> that provide convenient
/// entry points for keyset (cursor) pagination without requiring callers to
/// interact with <see cref="ISeekFactory"/> or <see cref="ISeekBuilder{T}"/>
/// directly.
/// </summary>
public static class IQueryableHelper
{
    /// <summary>
    /// Executes a keyset-paginated query by resolving <see cref="ISeekService"/>
    /// from the provided <paramref name="serviceProvider"/>.
    /// </summary>
    /// <remarks>
    /// Use this overload inside middleware or minimal-API handlers where an
    /// <see cref="IServiceProvider"/> is readily available but injecting
    /// <see cref="ISeekService"/> directly is inconvenient.
    /// </remarks>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The base queryable to paginate.</param>
    /// <param name="serviceProvider">
    /// The DI container used to resolve <see cref="ISeekService"/>.
    /// </param>
    /// <param name="request">
    /// The pagination request containing the cursor token and desired page size.
    /// </param>
    /// <param name="configure">
    /// A delegate that configures sort columns (and optionally the filter
    /// strategy) on the <see cref="ISeekBuilder{T}"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that can cancel the asynchronous database query.
    /// </param>
    /// <returns>
    /// A <see cref="SeekResult{T}"/> containing the page items and the
    /// next/previous cursor tokens.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/>, <paramref name="serviceProvider"/>,
    /// <paramref name="request"/>, or <paramref name="configure"/> is
    /// <see langword="null"/>.
    /// </exception>
    public static ValueTask<SeekResult<T>> ToSeekResultAsync<T>(this IQueryable<T> query, IServiceProvider serviceProvider, SeekRequest request, Action<ISeekBuilder<T>> configure, CancellationToken cancellationToken = default)
    {
        if (query           == null) throw new ArgumentNullException(nameof(query));
        if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));
        if (request         == null) throw new ArgumentNullException(nameof(request));
        if (configure       == null) throw new ArgumentNullException(nameof(configure));
        return serviceProvider
            .GetRequiredService<ISeekService>()
            .SeekAsync(query, request, configure, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Executes a keyset-paginated query using the provided
    /// <paramref name="seekFactory"/> to create and run the underlying
    /// <see cref="ISeekBuilder{T}"/>.
    /// </summary>
    /// <remarks>
    /// Use this overload when you have an <see cref="ISeekFactory"/> injected
    /// but prefer the fluent extension-method style over calling
    /// <c>seekFactory.CreateBuilder(query)</c> manually.
    /// </remarks>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The base queryable to paginate.</param>
    /// <param name="seekFactory">
    /// The factory used to create an <see cref="ISeekBuilder{T}"/> for this query.
    /// </param>
    /// <param name="request">
    /// The pagination request containing the cursor token and desired page size.
    /// </param>
    /// <param name="configure">
    /// A delegate that configures sort columns (and optionally the filter
    /// strategy) on the <see cref="ISeekBuilder{T}"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that can cancel the asynchronous database query.
    /// </param>
    /// <returns>
    /// A <see cref="SeekResult{T}"/> containing the page items and the
    /// next/previous cursor tokens.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/>, <paramref name="seekFactory"/>,
    /// <paramref name="request"/>, or <paramref name="configure"/> is
    /// <see langword="null"/>.
    /// </exception>
    public static ValueTask<SeekResult<T>> ToSeekResultAsync<T>(this IQueryable<T> query, ISeekFactory seekFactory, SeekRequest request, Action<ISeekBuilder<T>> configure, CancellationToken cancellationToken = default)
    {
        if (query       == null) throw new ArgumentNullException(nameof(query));
        if (seekFactory == null) throw new ArgumentNullException(nameof(seekFactory));
        if (request     == null) throw new ArgumentNullException(nameof(request));
        if (configure   == null) throw new ArgumentNullException(nameof(configure));

        var builder = seekFactory
            .CreateBuilder(query)
            .WithRequest(request);

        configure(builder);

        return builder.ToSeekResultAsync(cancellationToken);
    }

    /// <summary>
    /// Executes a keyset-paginated query by delegating directly to the
    /// provided <paramref name="seekService"/>.
    /// </summary>
    /// <remarks>
    /// This is the most straightforward overload when <see cref="ISeekService"/>
    /// is already injected into the class — it simply forwards the call.
    /// </remarks>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The base queryable to paginate.</param>
    /// <param name="seekService">
    /// The seek service that orchestrates ordering, cursor decoding, filtering,
    /// and result projection.
    /// </param>
    /// <param name="request">
    /// The pagination request containing the cursor token and desired page size.
    /// </param>
    /// <param name="configure">
    /// A delegate that configures sort columns (and optionally the filter
    /// strategy) on the <see cref="ISeekBuilder{T}"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that can cancel the asynchronous database query.
    /// </param>
    /// <returns>
    /// A <see cref="SeekResult{T}"/> containing the page items and the
    /// next/previous cursor tokens.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/>, <paramref name="seekService"/>,
    /// <paramref name="request"/>, or <paramref name="configure"/> is
    /// <see langword="null"/>.
    /// </exception>
    public static ValueTask<SeekResult<T>> ToSeekResultAsync<T>(this IQueryable<T> query, ISeekService seekService, SeekRequest request, Action<ISeekBuilder<T>> configure, CancellationToken cancellationToken = default)
    {
        if (query       == null) throw new ArgumentNullException(nameof(query));
        if (seekService == null) throw new ArgumentNullException(nameof(seekService));
        if (request     == null) throw new ArgumentNullException(nameof(request));
        if (configure   == null) throw new ArgumentNullException(nameof(configure));

        return seekService.SeekAsync(query, request, configure, cancellationToken);
    }

    /// <summary>
    /// Executes a keyset-paginated, projected query by resolving <see cref="ISeekService"/>
    /// from the provided <paramref name="serviceProvider"/>. Equivalent to calling
    /// <see cref="ISeekBuilder{T}.Select{TResult}"/> on the builder before executing it —
    /// <paramref name="transformer"/> only runs against the already ordered, filtered,
    /// and limited row set.
    /// </summary>
    /// <typeparam name="T">The entity type the query, ordering, and keyset filter operate on.</typeparam>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="query">The base queryable to paginate.</param>
    /// <param name="serviceProvider">The DI container used to resolve <see cref="ISeekFactory"/>.</param>
    /// <param name="request">The pagination request containing the cursor token and desired page size.</param>
    /// <param name="transformer">Projects the ordered, filtered, limited query to <typeparamref name="TResult"/>.</param>
    /// <param name="configure">A delegate that configures sort columns (and optionally the filter strategy) before the projection is applied.</param>
    /// <param name="cancellationToken">A token that can cancel the asynchronous database query.</param>
    /// <returns>A <see cref="SeekResult{TResult}"/> containing the projected page items and the next/previous cursor tokens.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/>, <paramref name="serviceProvider"/>,
    /// <paramref name="request"/>, <paramref name="transformer"/>, or
    /// <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static ValueTask<SeekResult<TResult>> ToSeekResultAsync<T, TResult>(this IQueryable<T> query, IServiceProvider serviceProvider, SeekRequest request, Func<IQueryable<T>, IQueryable<TResult>> transformer, Action<ISeekBuilder<T>> configure, CancellationToken cancellationToken = default)
    {
        if (query           == null) throw new ArgumentNullException(nameof(query));
        if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));
        if (request         == null) throw new ArgumentNullException(nameof(request));
        if (transformer     == null) throw new ArgumentNullException(nameof(transformer));
        if (configure       == null) throw new ArgumentNullException(nameof(configure));

        var builder = serviceProvider
            .GetRequiredService<ISeekFactory>()
            .CreateBuilder(query)
            .WithRequest(request);

        configure(builder);

        return builder.Select(transformer).ToSeekResultAsync(cancellationToken);
    }

    /// <summary>
    /// Executes a keyset-paginated, projected query using the provided
    /// <paramref name="seekFactory"/>. Equivalent to calling
    /// <see cref="ISeekBuilder{T}.Select{TResult}"/> on the builder before executing it —
    /// <paramref name="transformer"/> only runs against the already ordered, filtered,
    /// and limited row set.
    /// </summary>
    /// <typeparam name="T">The entity type the query, ordering, and keyset filter operate on.</typeparam>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="query">The base queryable to paginate.</param>
    /// <param name="seekFactory">The factory used to create an <see cref="ISeekBuilder{T}"/> for this query.</param>
    /// <param name="request">The pagination request containing the cursor token and desired page size.</param>
    /// <param name="transformer">Projects the ordered, filtered, limited query to <typeparamref name="TResult"/>.</param>
    /// <param name="configure">A delegate that configures sort columns (and optionally the filter strategy) before the projection is applied.</param>
    /// <param name="cancellationToken">A token that can cancel the asynchronous database query.</param>
    /// <returns>A <see cref="SeekResult{TResult}"/> containing the projected page items and the next/previous cursor tokens.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/>, <paramref name="seekFactory"/>,
    /// <paramref name="request"/>, <paramref name="transformer"/>, or
    /// <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static ValueTask<SeekResult<TResult>> ToSeekResultAsync<T, TResult>(this IQueryable<T> query, ISeekFactory seekFactory, SeekRequest request, Func<IQueryable<T>, IQueryable<TResult>> transformer, Action<ISeekBuilder<T>> configure, CancellationToken cancellationToken = default)
    {
        if (query       == null) throw new ArgumentNullException(nameof(query));
        if (seekFactory == null) throw new ArgumentNullException(nameof(seekFactory));
        if (request     == null) throw new ArgumentNullException(nameof(request));
        if (transformer == null) throw new ArgumentNullException(nameof(transformer));
        if (configure   == null) throw new ArgumentNullException(nameof(configure));

        var builder = seekFactory
            .CreateBuilder(query)
            .WithRequest(request);

        configure(builder);

        return builder.Select(transformer).ToSeekResultAsync(cancellationToken);
    }

    /// <summary>
    /// Executes a keyset-paginated, projected query by delegating directly to the
    /// provided <paramref name="seekService"/>. Equivalent to calling
    /// <see cref="ISeekBuilder{T}.Select{TResult}"/> on the builder before executing it —
    /// <paramref name="transformer"/> only runs against the already ordered, filtered,
    /// and limited row set.
    /// </summary>
    /// <typeparam name="T">The entity type the query, ordering, and keyset filter operate on.</typeparam>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="query">The base queryable to paginate.</param>
    /// <param name="seekService">The seek service used to create an <see cref="ISeekBuilder{T}"/> for this query.</param>
    /// <param name="request">The pagination request containing the cursor token and desired page size.</param>
    /// <param name="transformer">Projects the ordered, filtered, limited query to <typeparamref name="TResult"/>.</param>
    /// <param name="configure">A delegate that configures sort columns (and optionally the filter strategy) before the projection is applied.</param>
    /// <param name="cancellationToken">A token that can cancel the asynchronous database query.</param>
    /// <returns>A <see cref="SeekResult{TResult}"/> containing the projected page items and the next/previous cursor tokens.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/>, <paramref name="seekService"/>,
    /// <paramref name="request"/>, <paramref name="transformer"/>, or
    /// <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static ValueTask<SeekResult<TResult>> ToSeekResultAsync<T, TResult>(this IQueryable<T> query, ISeekService seekService, SeekRequest request, Func<IQueryable<T>, IQueryable<TResult>> transformer, Action<ISeekBuilder<T>> configure, CancellationToken cancellationToken = default)
    {
        if (query       == null) throw new ArgumentNullException(nameof(query));
        if (seekService == null) throw new ArgumentNullException(nameof(seekService));
        if (request     == null) throw new ArgumentNullException(nameof(request));
        if (transformer == null) throw new ArgumentNullException(nameof(transformer));
        if (configure   == null) throw new ArgumentNullException(nameof(configure));

        var builder = seekService
            .CreateBuilder(query)
            .WithRequest(request);

        configure(builder);

        return builder.Select(transformer).ToSeekResultAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a fluent <see cref="ISeekBuilder{T}"/> for <paramref name="query"/> by
    /// resolving <see cref="ISeekFactory"/> from the provided <paramref name="serviceProvider"/>.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The base queryable to paginate.</param>
    /// <param name="serviceProvider">The DI container used to resolve <see cref="ISeekFactory"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/> or <paramref name="serviceProvider"/> is <see langword="null"/>.
    /// </exception>
    public static ISeekBuilder<T> ToSeekBuilder<T>(this IQueryable<T> query, IServiceProvider serviceProvider)
    {
        if (query           == null) throw new ArgumentNullException(nameof(query));
        if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));

        return serviceProvider.GetRequiredService<ISeekFactory>().CreateBuilder(query);
    }

    /// <summary>
    /// Creates a fluent <see cref="ISeekBuilder{T}"/> for <paramref name="query"/>, pre-populated
    /// with <paramref name="request"/>, by resolving <see cref="ISeekFactory"/> from the
    /// provided <paramref name="serviceProvider"/>.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The base queryable to paginate.</param>
    /// <param name="serviceProvider">The DI container used to resolve <see cref="ISeekFactory"/>.</param>
    /// <param name="request">The pagination request containing the cursor token and desired page size.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/>, <paramref name="serviceProvider"/>, or
    /// <paramref name="request"/> is <see langword="null"/>.
    /// </exception>
    public static ISeekBuilder<T> ToSeekBuilder<T>(this IQueryable<T> query, IServiceProvider serviceProvider, SeekRequest request)
    {
        if (query           == null) throw new ArgumentNullException(nameof(query));
        if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));
        if (request         == null) throw new ArgumentNullException(nameof(request));

        return serviceProvider.GetRequiredService<ISeekFactory>().CreateBuilder(query).WithRequest(request);
    }

    /// <summary>
    /// Creates a fluent <see cref="ISeekBuilder{T}"/> for <paramref name="query"/> using the
    /// provided <paramref name="seekFactory"/>.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The base queryable to paginate.</param>
    /// <param name="seekFactory">The factory used to create the builder.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/> or <paramref name="seekFactory"/> is <see langword="null"/>.
    /// </exception>
    public static ISeekBuilder<T> ToSeekBuilder<T>(this IQueryable<T> query, ISeekFactory seekFactory)
    {
        if (query       == null) throw new ArgumentNullException(nameof(query));
        if (seekFactory == null) throw new ArgumentNullException(nameof(seekFactory));

        return seekFactory.CreateBuilder(query);
    }

    /// <summary>
    /// Creates a fluent <see cref="ISeekBuilder{T}"/> for <paramref name="query"/>, pre-populated
    /// with <paramref name="request"/>, using the provided <paramref name="seekFactory"/>.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The base queryable to paginate.</param>
    /// <param name="seekFactory">The factory used to create the builder.</param>
    /// <param name="request">The pagination request containing the cursor token and desired page size.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/>, <paramref name="seekFactory"/>, or
    /// <paramref name="request"/> is <see langword="null"/>.
    /// </exception>
    public static ISeekBuilder<T> ToSeekBuilder<T>(this IQueryable<T> query, ISeekFactory seekFactory, SeekRequest request)
    {
        if (query       == null) throw new ArgumentNullException(nameof(query));
        if (seekFactory == null) throw new ArgumentNullException(nameof(seekFactory));
        if (request     == null) throw new ArgumentNullException(nameof(request));

        return seekFactory.CreateBuilder(query).WithRequest(request);
    }

    /// <summary>
    /// Creates a fluent <see cref="ISeekBuilder{T}"/> for <paramref name="query"/> by
    /// delegating directly to the provided <paramref name="seekService"/>.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The base queryable to paginate.</param>
    /// <param name="seekService">The seek service used to create the builder.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/> or <paramref name="seekService"/> is <see langword="null"/>.
    /// </exception>
    public static ISeekBuilder<T> ToSeekBuilder<T>(this IQueryable<T> query, ISeekService seekService)
    {
        if (query       == null) throw new ArgumentNullException(nameof(query));
        if (seekService == null) throw new ArgumentNullException(nameof(seekService));

        return seekService.CreateBuilder(query);
    }

    /// <summary>
    /// Creates a fluent <see cref="ISeekBuilder{T}"/> for <paramref name="query"/>, pre-populated
    /// with <paramref name="request"/>, by delegating directly to the provided
    /// <paramref name="seekService"/>.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The base queryable to paginate.</param>
    /// <param name="seekService">The seek service used to create the builder.</param>
    /// <param name="request">The pagination request containing the cursor token and desired page size.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/>, <paramref name="seekService"/>, or
    /// <paramref name="request"/> is <see langword="null"/>.
    /// </exception>
    public static ISeekBuilder<T> ToSeekBuilder<T>(this IQueryable<T> query, ISeekService seekService, SeekRequest request)
    {
        if (query       == null) throw new ArgumentNullException(nameof(query));
        if (seekService == null) throw new ArgumentNullException(nameof(seekService));
        if (request     == null) throw new ArgumentNullException(nameof(request));

        return seekService.CreateBuilder(query).WithRequest(request);
    }
}
