# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2026-07-13

See the [migration guide](docs/MIGRATION-2.0.md) for upgrading from 1.x.

### Added

- **SeekKit.MongoDB** — new package: cursor (keyset) pagination for MongoDB
  over the official driver's LINQ provider. `AddSeekKitMongo()`,
  `ISeekMongoService`, fluent `ISeekMongoBuilder<T>`, and built-in `ObjectId`
  converters. Shares tokens and result contracts with SeekKit.EntityFramework.
- **SeekKit.Core** — new package holding the provider-agnostic core:
  `SeekResult<T>`, `SeekRequest`, `SeekData`, `SeekDirection`, `PageMetadata`,
  token serializers (including HMAC signing), type converters, the OR-logic
  keyset strategy, and the shared pagination algorithm (`SeekBuilderBase<T>`).
- `AddSeekKitCore()` — providers can now be combined in one application:
  `AddSeekKit` + `AddSeekKitMongo` share a single type-converter registry and
  token serializer regardless of registration order.
- SeekKit.MongoDB: paginate an `IAggregateFluent<T>` (e.g.
  `collection.Aggregate().UnionWith(other)` or pipelines with `$lookup`/custom
  stages) via `ISeekMongoService.SeekAsync(IAggregateFluent<T>, ...)`. SeekKit
  appends a keyset `$match`, `$sort`, and `$limit`.
- SeekKit.MongoDB: paginate an `IFindFluent<T, T>` (e.g.
  `collection.Find(bsonFilter)` with a BSON `FilterDefinition` — text/geo/`$expr`
  filters the LINQ provider can't express) via
  `ISeekMongoService.SeekAsync(IFindFluent<T, T>, ...)`. SeekKit AND-s the keyset
  predicate into the query filter and appends a sort and a limit.
- `KeysetPredicateBuilder` (SeekKit.Core) exposes the OR-logic keyset predicate
  for reuse across LINQ (`$where`) and aggregation (`$match`) paths.

### Changed

- **Breaking:** shared types moved out of `SeekKit.EntityFramework.*`
  namespaces into the new SeekKit.Core package — `SeekResult<T>`,
  `SeekRequest`, `SeekData`, `SeekDirection`, `PageMetadata` now live in
  `SeekKit.Core.Models`; `ISeekSerializer`, `TypeConverter<T>`,
  `ISeekFilterStrategy`, `SeekKitConfiguration` and friends in
  `SeekKit.Core`; converters in `SeekKit.Core.Converters`. Update your
  `using` directives — no API shapes changed.
- A malformed or tampered pagination token now falls back to the first page
  instead of throwing at query time.

### Fixed

- `SeekResult<T>.WithValue(...)` data was silently dropped from serialized
  responses — the `[JsonExtensionData]` property was non-public, so
  System.Text.Json ignored it. It is now public and serializes as documented.

## [1.0.1] - 2026-07-06

### Fixed

- NuGet package now ships a Markdown-only README so it renders correctly on
  nuget.org (the repo README uses HTML, which nuget.org does not render)

## [1.0.0] - 2026-07-06

First open-source release. 🎉

### Added

- Cursor (keyset) pagination for Entity Framework Core with opaque, bidirectional tokens
- Database-tuned query strategies:
  - PostgreSQL: `Auto`, `Tuple` (row-value comparison), `UnionAll`, `OrLogic`
  - SQL Server / MySQL / Oracle / SQLite: `UnionAll`, `OrLogic`
  - Configurable fallback (`UnionAll` / `OrLogic` / `None`)
- Fluent builder (`ISeekBuilder<T>`) and one-call service (`ISeekService.SeekAsync`)
- Multi-column sorting with mixed ascending/descending directions
- `SeekResult<T>.Map` for DTO projection preserving pagination metadata
- `SeekResult<T>.WithValue` for attaching extra response properties
- Optional HMAC-SHA256 token signing (`config.UseHmacSigning(key)`) to reject
  tampered or forged pagination tokens
- Extensibility points: custom type converters, custom token serializer
  (`ISeekSerializer`), custom filter strategy (`ISeekFilterStrategy`)
- Built-in converters for all primitives, `Guid`, `DateTime`, `DateTimeOffset`,
  `DateOnly`, `TimeOnly`, `TimeSpan`, and nullable variants
- Targets: .NET Standard 2.1 (EF Core 5), .NET 8 / 9 / 10 (EF Core 8 / 9 / 10)
- Example project: .NET 10 minimal API + SQL Server via Docker Compose +
  bulk seed script for seek-vs-offset benchmarking

[2.0.0]: https://github.com/lttruc1402/SeekKit.EntityFramework/releases/tag/v2.0.0
[1.0.1]: https://github.com/lttruc1402/SeekKit.EntityFramework/releases/tag/v1.0.1
[1.0.0]: https://github.com/lttruc1402/SeekKit.EntityFramework/releases/tag/v1.0.0
