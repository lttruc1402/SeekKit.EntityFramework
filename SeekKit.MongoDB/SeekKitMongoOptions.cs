namespace SeekKit.MongoDB;

/// <summary>
/// Global configuration options for SeekKit cursor pagination with MongoDB.
/// Register via <c>services.AddSeekKitMongo(options => { ... })</c>.
/// Page-size options are inherited from <see cref="SeekOptionsBase"/>.
/// </summary>
public sealed class SeekKitMongoOptions : SeekOptionsBase
{
    public SeekKitMongoOptions() { }

    public SeekKitMongoOptions(SeekKitMongoOptions options) : base(options) { }
}
