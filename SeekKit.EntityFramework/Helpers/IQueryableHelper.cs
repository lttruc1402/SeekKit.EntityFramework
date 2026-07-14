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



    public static ISeekBuilder<T> ToSeekBuilder<T>(this IQueryable<T> query, IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<ISeekFactory>().CreateBuilder(query);
    }


    public static ISeekBuilder<T> ToSeekBuilder<T>(this IQueryable<T> query, IServiceProvider serviceProvider, SeekRequest request)
    {
        return serviceProvider.GetRequiredService<ISeekFactory>().CreateBuilder(query).WithRequest(request);
    }
}
