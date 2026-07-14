namespace SeekKit.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the provider-agnostic SeekKit services — type-converter
    /// registry, value converter, and token serializer — exactly once.
    /// <para>
    /// Called internally by <c>AddSeekKit</c> (SeekKit.EntityFramework) and
    /// <c>AddSeekKitMongo</c> (SeekKit.MongoDB), so it is safe to register
    /// multiple providers in the same application: they all share one
    /// converter registry and one token serializer, regardless of
    /// registration order.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optionally registers additional type converters or replaces the token
    /// serializer. Converters land in the shared registry and are visible to
    /// every provider.
    /// </param>
    public static IServiceCollection AddSeekKitCore(
        this IServiceCollection services,
        Action<SeekKitConfiguration>? configure = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        // Reuse the registry created by an earlier AddSeekKit* call so every
        // provider contributes converters to the same instance.
        TypeConverterRegistry registry;
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(ITypeConverterRegistry));
        if (existing?.ImplementationInstance is TypeConverterRegistry shared)
        {
            registry = shared;
        }
        else
        {
            registry = TypeConverterRegistry.CreateDefault();
            services.AddSingleton<ITypeConverterRegistry>(registry);
        }

        services.TryAddSingleton<ISeekValueConverter, SeekValueConverter>();
        services.TryAddSingleton<ISeekSerializer, SeekSerializer>();

        configure?.Invoke(new SeekKitConfiguration(registry, services));

        return services;
    }
}
