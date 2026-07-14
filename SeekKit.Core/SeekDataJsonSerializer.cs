namespace SeekKit.Core;

#if NET6_0_OR_GREATER
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(SeekData))]
internal sealed partial class SeekDataJsonSerializer : JsonSerializerContext
{
}
#endif
