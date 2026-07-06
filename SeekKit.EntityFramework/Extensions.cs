namespace SeekKit.EntityFramework;
public static class Extensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers SeekKit cursor pagination services.
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

           
            TypeConverterRegistry registry = new();
            RegisterBuiltInConverters(registry);
            if (configure != null)
            {
                SeekKitConfiguration seekKitConfiguration = new(registry, services);
                configure(seekKitConfiguration);
            }

            return services
                .Configure(configureOptions)
                .AddSingleton<ITypeConverterRegistry>(registry)
                .AddSingleton<ISeekValueConverter, SeekValueConverter>()
                .AddSingleton<ISeekSerializer, SeekSerializer>()
                .AddSingleton<ISeekFactory, SeekFactory>()
                .AddSingleton<ISeekService, SeekService>();
        }
    }

    private static void RegisterBuiltInConverters(TypeConverterRegistry registry)
    {
        registry.Register(new ByteConverter());
        registry.Register(new NullableByteConverter());
        registry.Register(new SByteConverter());
        registry.Register(new NullableSByteConverter());

        registry.Register(new ShortConverter());
        registry.Register(new NullableShortConverter());
        registry.Register(new UShortConverter());
        registry.Register(new NullableUShortConverter());

        registry.Register(new IntConverter());
        registry.Register(new NullableIntConverter());
        registry.Register(new UIntConverter());
        registry.Register(new NullableUIntConverter());

        registry.Register(new LongConverter());
        registry.Register(new NullableLongConverter());
        registry.Register(new ULongConverter());
        registry.Register(new NullableULongConverter());

        registry.Register(new FloatConverter());
        registry.Register(new NullableFloatConverter());
        registry.Register(new DoubleConverter());
        registry.Register(new NullableDoubleConverter());
        registry.Register(new DecimalConverter());
        registry.Register(new NullableDecimalConverter());

        registry.Register(new BoolConverter());
        registry.Register(new NullableBoolConverter());
        registry.Register(new CharConverter());
        registry.Register(new NullableCharConverter());
        registry.Register(new StringConverter());

        registry.Register(new DateTimeConverter());
        registry.Register(new NullableDateTimeConverter());
        registry.Register(new DateTimeOffsetConverter());
        registry.Register(new NullableDateTimeOffsetConverter());

#if NET6_0_OR_GREATER
        registry.Register(new DateOnlyConverter());
        registry.Register(new NullableDateOnlyConverter());
        registry.Register(new TimeOnlyConverter());
        registry.Register(new NullableTimeOnlyConverter());
#endif

        registry.Register(new TimeSpanConverter());
        registry.Register(new NullableTimeSpanConverter());

        registry.Register(new GuidConverter());
        registry.Register(new NullableGuidConverter());
    }
}
