namespace SeekKit.EntityFramework;
public static class Extensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers SeekKit cursor pagination services for EF Core.
        /// <para>
        /// Safe to combine with <c>AddSeekKitMongo</c> (SeekKit.MongoDB) in the
        /// same application: both providers share one type-converter registry
        /// and one token serializer, regardless of registration order.
        /// </para>
        /// </summary>
        /// <param name="configureOptions">Configure database strategy and page sizes.</param>
        /// <param name="configure">Optionally register custom type converters or a custom serializer.</param>
        public IServiceCollection AddSeekKit(
            Action<SeekKitOptions> configureOptions,
            Action<SeekKitConfiguration>? configure = null)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(services, nameof(services));
            ArgumentNullException.ThrowIfNull(configureOptions, nameof(configureOptions));
#else
            if (services is null) throw new ArgumentNullException(nameof(services));
            if (configureOptions is null) throw new ArgumentNullException(nameof(configureOptions));
#endif

            services.AddSeekKitCore(configure);

            services.Configure(configureOptions);
            services.TryAddSingleton<ISeekFactory, SeekFactory>();
            services.TryAddSingleton<ISeekService, SeekService>();

            return services;
        }
    }
}
