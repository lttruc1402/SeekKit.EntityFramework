# SeekKit.Core

Provider-agnostic core for **SeekKit** cursor (keyset) pagination. You normally
don't install this package directly — it comes as a dependency of a provider
package:

| Provider | Package |
|----------|---------|
| Entity Framework Core | `SeekKit.EntityFramework` |
| MongoDB | `SeekKit.MongoDB` |

## What's inside

- **Shared contracts** — `SeekResult<T>`, `SeekRequest`, `SeekData`,
  `SeekDirection`, `PageMetadata`. Because every provider package shares these
  types, a service can paginate SQL and MongoDB data behind one API shape.
- **Opaque token serialization** — URL-safe Base64 tokens with an optional
  HMAC-SHA256-signed serializer (`config.UseHmacSigning(key)`).
- **Type converters** — stable string round-tripping for all primitives,
  `Guid`, date/time types, and nullable variants; extensible via
  `TypeConverter<T>`.
- **LINQ keyset strategies** — provider-neutral expression building, including
  the OR-expanded predicate strategy that works on any LINQ provider.

## Links

- **Documentation**: https://github.com/lttruc1402/SeekKit.EntityFramework
- **Changelog**: https://github.com/lttruc1402/SeekKit.EntityFramework/blob/master/CHANGELOG.md

Licensed under the [MIT License](https://github.com/lttruc1402/SeekKit.EntityFramework/blob/master/LICENSE).
