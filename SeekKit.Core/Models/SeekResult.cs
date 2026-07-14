namespace SeekKit.Core.Models;

/// <summary>
/// Represents a single page of results returned by SeekKit cursor pagination.
/// </summary>
/// <typeparam name="T">The type of items in the page.</typeparam>
public sealed class SeekResult<T>
{
    /// <summary>The items on the current page.</summary>
    public IReadOnlyList<T> Items { get; set; } = [];

    /// <summary>
    /// Opaque token to retrieve the next page. Pass this value as <see cref="SeekRequest.Token"/>.
    /// <c>null</c> when there are no more items after this page.
    /// </summary>
    public string? NextToken { get; set; }

    /// <summary>
    /// Opaque token to retrieve the previous page. Pass this value as <see cref="SeekRequest.Token"/>.
    /// <c>null</c> when this is the first page.
    /// </summary>
    public string? PreviousToken { get; set; }

    /// <summary>Indicates whether a next page exists.</summary>
    public bool HasNext { get; set; }

    /// <summary>Indicates whether a previous page exists.</summary>
    public bool HasPrevious { get; set; }

    /// <summary>Number of items actually returned on this page.</summary>
    public int Count { get; set; }

    /// <summary>Metadata about the page size and request time.</summary>
    public required PageMetadata PageMetadata { get; set; }

    /// <summary>
    /// Extra key-value pairs attached via <see cref="WithValue"/>, serialized as
    /// top-level JSON properties. Must be public for <see cref="JsonExtensionDataAttribute"/>
    /// to be honored by System.Text.Json.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, object>? ExtensionData { get; set; }

    /// <summary>
    /// Attaches an arbitrary key-value pair to the result. The value is serialized
    /// alongside the standard properties via <see cref="JsonExtensionDataAttribute"/>.
    /// </summary>
    /// <param name="key">The JSON property name.</param>
    /// <param name="value">The value to attach.</param>
    public SeekResult<T> WithValue(string key, object value)
    {
        ExtensionData ??= new Dictionary<string, object>();
        ExtensionData.Add(key, value);
        return this;
    }

    /// <summary>
    /// Projects each item to a new type, preserving all pagination metadata.
    /// Useful for mapping domain entities to DTOs without losing token information.
    /// </summary>
    /// <typeparam name="TDestination">The target type.</typeparam>
    /// <param name="mapper">A function that maps a single item.</param>
    public SeekResult<TDestination> Map<TDestination>(Func<T, TDestination> mapper)
    {
        var items = Items;
        var c = items.Count;
        var destinations = new TDestination[c];
        Span<TDestination> span = destinations.AsSpan();

        for (int i = 0; i < c; i++)
        {
            span[i] = mapper(items[i]);
        }

        return new SeekResult<TDestination>
        {
            Items = destinations,
            NextToken = NextToken,
            PreviousToken = PreviousToken,
            HasNext = HasNext,
            HasPrevious = HasPrevious,
            Count = items.Count,
            PageMetadata = PageMetadata,
            ExtensionData = ExtensionData
        };
    }
}
