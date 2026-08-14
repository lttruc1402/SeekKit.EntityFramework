# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.3.0] - 2026-08-14

All three packages release together at 2.3.0.

### Fixed

- **SeekKit.Core:** `OrderBy`/`OrderByDescending` threw `System.ArgumentException:
  Expression 'x' must be a property access` when sorting a scalar sequence (e.g.
  `IQueryable<int>`) by the element itself (`x => x`) instead of a property —
  common when pre-filtering to a set of ids before joining. Identity selectors are
  now supported for the plain (non-`Select()`) pagination path.

### Added

- **SeekKit.EntityFramework / SeekKit.MongoDB:** `OrderBy`/`OrderByDescending` gained
  an optional `resultPropertyName` parameter. Required only when combining an
  identity selector (`x => x`) with `Select()`: the projected `TResult` shape has
  no property corresponding to an identity selector, so the matching property name
  (e.g. `"Id"`) must be supplied explicitly so SeekKit can read the cursor value
  back after projection. Attempting this combination without it now throws a
  targeted `InvalidOperationException` naming the fix, instead of a confusing
  "no public property '$self'" error.

## [2.2.1] - 2026-08-13

All three packages (SeekKit.Core, SeekKit.EntityFramework, SeekKit.MongoDB) release
together at 2.2.1 going forward — see **Fixed** below for why.

### Fixed

- **Critical:** installing `SeekKit.EntityFramework` or `SeekKit.MongoDB` 2.2.0 threw
  `System.IO.FileNotFoundException: Could not load file or assembly 'SeekKit.Core,
  Version=2.2.0.0, ...'` at runtime. Root cause: the release workflow builds/packs the
  whole solution with a single `-p:Version=$TAG` MSBuild global property, which applies
  to *every* project — including `SeekKit.Core`, which wasn't part of that release and
  stayed declared at 2.1.0 in its own `.csproj`. That global override silently stamped
  the compiled `SeekKit.Core.dll`'s `AssemblyVersion` as `2.2.0.0` (the SDK derives
  `AssemblyVersion` from `Version` by default), while the actually-published
  `SeekKit.Core` package (and its dependency line in the EF/Mongo `.nuspec`, which reads
  `SeekKit.Core.csproj`'s own `<Version>` rather than the command-line override)
  remained at `2.1.0.0` — a strong assembly-binding mismatch that only surfaces once a
  consumer installs the package and runs it, not at CI build/test time. Fixed by:
  - Pinning `<AssemblyVersion>` to a fixed value in `Directory.Build.props`, decoupled
    from `<Version>`/`<PackageVersion>`, so a CI-wide version override (or any future
    per-package version drift) can never desync the strong assembly reference again.
    It now changes only for an actual breaking binary change, alongside a major bump.
  - Moving `<Version>`/`<PackageVersion>` themselves into `Directory.Build.props` as a
    single source shared by all three packable projects, so they always release
    together in lockstep — the independent per-package versioning that let this drift
    happen in the first place is gone.

## [2.2.0] - 2026-07-25

`SeekKit.EntityFramework` and `SeekKit.MongoDB` release together at 2.2.0.
`SeekKit.Core` is unaffected and stays at 2.1.0.

### Added

- **SeekKit.EntityFramework:** `ISeekService.SeekAsync<T, TResult>(query, request,
  transformer, configure[, configureOption], ct)` — one-call projected pagination
  using `ISeekBuilder<T>.Select<TResult>` without building the fluent chain
  yourself. Mirrors the existing non-projected `SeekAsync<T>` overloads exactly,
  including the per-request `SeekKitOptions` override variant.
- **SeekKit.MongoDB:** matching `ISeekMongoService.SeekAsync<T, TResult>(...)`
  overloads for all four sources — `IMongoCollection<T>`, `IQueryable<T>`,
  `IAggregateFluent<T>`, `IFindFluent<T, T>` — each taking the transformer shape
  that source's `Select<TResult>` expects.

## [2.1.0] - 2026-07-25

All three packages (SeekKit.Core, SeekKit.EntityFramework, SeekKit.MongoDB) release
together at 2.1.0.

### Added

- **SeekKit.EntityFramework:** `ISeekBuilder<T>.Select<TResult>(transformer)` — defers a
  join/projection until after ordering, keyset filtering, and the look-ahead `Take` have
  been applied, so the transform only runs against the already-limited row set instead
  of the full table (one database round trip). Returns `ISeekBuilder<T, TResult>`, whose
  `ToSeekResultAsync()` yields a `SeekResult<TResult>`. `TResult` must expose public
  properties with the same names and CLR types as the sort columns registered via
  `OrderBy`/`OrderByDescending`.
- **SeekKit.Core:** `SeekPagingAlgorithm` (shared look-ahead pagination algorithm,
  extracted from `SeekBuilderCore<T>`), `ResultKeyAccessor` (reflection-based cursor
  value reader for projected types), and `SeekProjectionBuilderBase<T, TResult>` — new
  reusable building blocks backing the `Select` feature above.
- **SeekKit.EntityFramework:** `IQueryableHelper` gained matching overloads for the new
  `Select` projection feature and for the existing `ISeekFactory`/`ISeekService`
  entry points:
  - `IQueryable<T>.ToSeekResultAsync<T, TResult>(..., transformer, configure, ...)` —
    one-call projected pagination, available via `IServiceProvider`, `ISeekFactory`,
    or `ISeekService`.
  - `IQueryable<T>.ToSeekBuilder<T>(ISeekFactory[, SeekRequest])` and
    `IQueryable<T>.ToSeekBuilder<T>(ISeekService[, SeekRequest])` — previously only
    available via `IServiceProvider`.
- **SeekKit.MongoDB:** `Select<TResult>` projection on all three builder origins —
  queryable/collection (`ISeekMongoQueryableBuilder<T>`), aggregation pipeline
  (`ISeekMongoAggregateBuilder<T>`), and find (`ISeekMongoFindBuilder<T>`). Each defers
  its join/projection until after the keyset filter, sort, and limit have been applied,
  reusing the `SeekKit.Core` building blocks above. `TResult` must expose public
  properties with the same names and CLR types as the sort columns.

### Changed

- **SeekKit.MongoDB:** `ISeekMongoService.CreateBuilder<T>(...)` now returns an
  origin-specific interface instead of the shared `ISeekMongoBuilder<T>`:
  `CreateBuilder(IMongoCollection<T>)`/`CreateBuilder(IQueryable<T>)` →
  `ISeekMongoQueryableBuilder<T>`; `CreateBuilder(IAggregateFluent<T>)` →
  `ISeekMongoAggregateBuilder<T>`; `CreateBuilder(IFindFluent<T, T>)` →
  `ISeekMongoFindBuilder<T>`. All three inherit `ISeekMongoBuilder<T>`, so ordinary
  usage (`var builder = ...`, or assigning the result to an `ISeekMongoBuilder<T>`-typed
  variable/parameter) keeps compiling unchanged — this is a source-compatible upcast.
  The one exception is test code that mocks `ISeekMongoService.CreateBuilder` and
  declares the mock's return type as the literal `ISeekMongoBuilder<T>`; that needs to
  update to the narrower type.
- **SeekKit.MongoDB:** `WithStrategy` moved off the shared `ISeekMongoBuilder<T>`
  interface onto `ISeekMongoQueryableBuilder<T>` only — it's the only origin with a
  real alternative strategy to switch to (Mongo has no aggregate/find-specific
  `ISeekFilterStrategy` implementations). Calling it on the aggregate or find builder
  is now a **compile error** instead of a runtime `NotSupportedException` — every call
  site that compiled and ran successfully before still does; only code that was already
  guaranteed to throw at runtime is affected.

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
