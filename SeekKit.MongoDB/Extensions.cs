namespace SeekKit.MongoDB;

public static class Extensions
{
    /// <summary>
    /// Registers SeekKit cursor pagination services for MongoDB.
    /// <para>
    /// Safe to combine with <c>AddSeekKit</c> (SeekKit.EntityFramework) in the
    /// same application: both providers share one type-converter registry and
    /// one token serializer, regardless of registration order.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Configure page sizes.</param>
    /// <param name="configure">Optionally register custom type converters or a custom serializer.</param>
    public static IServiceCollection AddSeekKitMongo(
        this IServiceCollection services,
        Action<SeekKitMongoOptions>? configureOptions = null,
        Action<SeekKitConfiguration>? configure = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.AddSeekKitCore(cfg =>
        {
            cfg.AddConverter(new ObjectIdConverter());
            cfg.AddConverter(new NullableObjectIdConverter());
            configure?.Invoke(cfg);
        });

        services.Configure(configureOptions ?? (_ => { }));
        services.TryAddSingleton<ISeekMongoService, SeekMongoService>();

        return services;
    }
}
