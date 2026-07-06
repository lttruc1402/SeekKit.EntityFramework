namespace SeekKit.EntityFramework.Core;

/// <summary>
/// Factory for creating <see cref="ISeekBuilder{T}"/> instances with full control over
/// <see cref="SeekKitOptions"/>. Use <see cref="ISeekService"/> for the common case;
/// inject this factory when you need to override options programmatically per query
/// (e.g. switching database or strategy at runtime).
/// </summary>
public interface ISeekFactory
{
    /// <summary>
    /// Creates a builder using the globally configured <see cref="SeekKitOptions"/>.
    /// </summary>
    /// <typeparam name="T">The entity type to paginate.</typeparam>
    /// <param name="query">The base EF Core query to paginate.</param>
    ISeekBuilder<T> CreateBuilder<T>(IQueryable<T> query);

    /// <summary>
    /// Creates a builder using an explicit <see cref="SeekKitOptions"/> instance,
    /// replacing the global configuration entirely for this builder.
    /// </summary>
    /// <typeparam name="T">The entity type to paginate.</typeparam>
    /// <param name="query">The base EF Core query to paginate.</param>
    /// <param name="options">The options to use for this builder.</param>
    ISeekBuilder<T> CreateBuilder<T>(IQueryable<T> query, SeekKitOptions options);

    /// <summary>
    /// Creates a builder that copies the global <see cref="SeekKitOptions"/> and
    /// applies <paramref name="configure"/> to override individual settings.
    /// </summary>
    /// <typeparam name="T">The entity type to paginate.</typeparam>
    /// <param name="query">The base EF Core query to paginate.</param>
    /// <param name="configure">A delegate that mutates a copy of the global options.</param>
    ISeekBuilder<T> CreateBuilder<T>(IQueryable<T> query, Action<SeekKitOptions> configure);
}

internal sealed class SeekFactory : ISeekFactory
{
    private readonly IServiceProvider    _serviceProvider;
    private readonly ISeekSerializer     _seekSerializer;
    private readonly ISeekValueConverter _valueConverter;

    public SeekFactory(IServiceProvider serviceProvider, ISeekSerializer seekSerializer, ISeekValueConverter valueConverter)
    {
        _serviceProvider = serviceProvider;
        _seekSerializer  = seekSerializer;
        _valueConverter  = valueConverter;
    }

    public ISeekBuilder<T> CreateBuilder<T>(IQueryable<T> query)
        => new SeekBuilder<T>(query, _seekSerializer, _valueConverter,
               _serviceProvider.GetRequiredService<IOptions<SeekKitOptions>>().Value);

    public ISeekBuilder<T> CreateBuilder<T>(IQueryable<T> query, SeekKitOptions options)
        => new SeekBuilder<T>(query, _seekSerializer, _valueConverter, options);

    public ISeekBuilder<T> CreateBuilder<T>(IQueryable<T> query, Action<SeekKitOptions> configure)
    {
        var options = new SeekKitOptions(_serviceProvider.GetRequiredService<IOptions<SeekKitOptions>>().Value);
        configure(options);
        return CreateBuilder(query, options);
    }
}