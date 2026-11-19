# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.4.0] - 2026-11-19

All three packages release together at 2.4.0.

### Added

- **All packages:** new `net11.0` target (EF Core 11 for SeekKit.EntityFramework).
  The existing `netstandard2.1`, `net8.0`, `net9.0`, and `net10.0` targets are
  unchanged.
- **SeekKit.Core:** `Select()` (EF) / the queryable `Select()` origin (Mongo) now
  **traces each sort column's value through the transformer** to find the
  `TResult` property that holds it, whenever `TResult` has no same-named
  property. This replaces the 2.3.0 auto-detection, which only recognized a
  single `Join` or a single `Select`. Tracing follows `Select`, `Join`,
  `LeftJoin`, `RightJoin`, `GroupJoin` + `SelectMany`, and any element-preserving
  operator (`Where`, `OrderBy`, `Take`, …) through any number of intermediate
  anonymous types, object initializers, and positional records, and uses join
  key equality (`id => id` ⇄ `o => o.Id`, including composite keys). In practice:
  - an identity sort (`OrderBy(x => x)` on pre-filtered ids) followed by a chain
    of joins no longer needs `resultPropertyName`;
  - ordinary member sorts work when the projection renames the column
    (`OrderBy(o => o.CreatedAt)` + `new Dto { Created = o.CreatedAt }`).

  Like before, the transformer is invoked once against an empty in-memory
  queryable and only its expression tree is inspected — no query runs. Tracing
  never guesses: a computed value, a value read from the optional side of an
  outer join, or a projection built outside LINQ is reported as untraceable and
  `Select()` throws the same actionable `InvalidOperationException` as before.
- **Client-driven sorting:** `SeekSortMap<T>` (SeekKit.Core) is a whitelist that
  turns a sort string such as `price:desc,name:asc` into sort columns, with a default
  sort, an automatic unique tie-breaker, and a field limit. Apply it with
  `builder.ApplySort(sortMap, sort)` on any EF or MongoDB builder. Unknown
  fields and bad syntax throw the new `SeekSortException` (an
  `ArgumentException`) — map it to `400`.
- **Nullable sort columns** are supported on every strategy and every MongoDB
  origin. The keyset filter is made null-aware (`col > @p OR col IS NULL`,
  `col IS NOT NULL`, …) while the `ORDER BY` is left untouched. Where the
  database puts `NULL` comes from the new `NullOrdering` option, which defaults
  per provider: `NullsLargest` for PostgreSQL and Oracle, `NullsSmallest` for
  SQL Server, MySQL, SQLite, and MongoDB.
- **Computed sort keys:** `OrderBy` / `OrderByDescending` accept any expression
  the provider can translate (`x => x.Name.ToLower()`, `x => x.Price * x.Qty`)
  instead of throwing "must be a property access". Supported by EF Core and the
  MongoDB queryable origin; the MongoDB aggregate/find builders throw a
  descriptive `NotSupportedException`. With `Select()`, pass
  `resultPropertyName` for such a column.
- **Total count:** `WithTotalCount()` on every builder fills the new
  `SeekResult<T>.TotalCount` (`long?`, omitted from JSON when null) with one
  extra count query over the unpaged source.
- **Streaming:** `ToPagesAsync()` and `ToItemsAsync()` on every builder
  (including the projected ones) return an `IAsyncEnumerable` that walks from
  the requested page to the last, one lazy keyset query per page.
- **Token validation:** every token now carries a fingerprint of the sort it
  was issued for. New options `InvalidTokenBehavior` (`FirstPage` | `Throw`)
  and `TokenLifetime` (token expiry), and a new `SeekTokenException` with an
  `Error` of `SortMismatch`, `InvalidValue`, `Malformed`, or `Expired`.
- **Tests:** four new integration test projects run every sort flavour, forward
  and backward, against the real database in a container (Testcontainers), under
  every strategy: `SeekKit.EntityFramework.PostgreSql.IntegrationTests`
  (PostgreSQL 17 — sorts `NULL` last, has row-value comparison),
  `…SqlServer.IntegrationTests` (SQL Server 2022), `…MySql.IntegrationTests`
  (MySQL 8.4, `MySql.EntityFrameworkCore`), and `…Oracle.IntegrationTests`
  (Oracle Database 23 Free). The last three share one provider-independent
  suite. SQL Server runs on EF Core 10 and 11; the others target `net10.0` only
  until their providers ship a stable release for EF Core 11.
- **Examples:** both example APIs gained `GET /products/sorted?sort=…`, showing
  `SeekSortMap`, `WithTotalCount`, and mapping the two exceptions to `400`.

### Performance

Measured, not estimated — see "Measured: why the strategy matters" in the README.

- **Sort-column accessors are compiled once per process** instead of on every
  `OrderBy` call. Building a builder with two sort columns dropped from ~131 µs
  and 9.9 KB to ~3.5 µs and 1.5 KB per request (four columns: 244 µs → 5.2 µs).
  Computed sort keys are interpreted rather than JIT-compiled.
- **PostgreSQL: `ForPostgreSql` now defaults to `FallbackStrategy.UnionAll`**
  (was `OrLogic`). The fallback is what runs whenever the row-value comparison
  can't — mixed sort directions, and now nullable columns. On a 2M-row table a
  page from the middle took 85 ms–1.4 s through the OR predicate and takes
  1.2–2.0 ms through `UNION ALL`. Pass `fallback: FallbackStrategy.OrLogic` to
  get the old behaviour back.
- **`UNION ALL`: a nullable column's `IS NULL` alternative is its own branch**
  rather than an `OR` inside one, so every branch stays a plain index range.
- **`UNION ALL`: each branch is ordered only by the columns it doesn't pin.** A
  branch filtering on `a = @a AND b > @b` is now ordered by `b, c` instead of
  `a, b, c` (the outer query still orders by all of them). MySQL does not see
  through `WHERE a IS NULL ORDER BY a, b` and sorted every matching row — 780 ms
  for a page that now takes 5 ms.

### Fixed

- **String sort columns** only worked on the first page: building the keyset
  filter for the next one threw `InvalidOperationException: The binary operator
  GreaterThan is not defined for the types 'System.String' and 'System.String'`
  on the OR-predicate and `UNION ALL` strategies. Strings are now compared with
  `string.Compare`, which EF Core and the MongoDB driver translate to a plain
  column comparison.
- **Nullable sort columns** silently lost every row holding `NULL` from the
  second page on, and a `NULL` boundary value was written into the token as an
  empty string (indistinguishable from `""` for strings). See **Added** above;
  `NULL` is now stored in the token as JSON `null`.
- A malformed token correctly fell back to the first page but reported
  `HasPrevious = true` and returned a `PreviousToken`. The fallback page is now
  a real first page.
- **SeekKit.MongoDB:** the find builders AND-ed each page's keyset predicate
  into the caller's mutable `IFindFluent`, so reusing one builder for several
  pages stacked predicates. Each fetch now starts from the original filter.

### Changed

- A token used with a different sort than it was issued for now throws
  `SeekTokenException` (`SortMismatch`). Previously this either threw a plain
  `InvalidOperationException` ("Cursor value for 'X' not found") or — when the
  column names happened to match — silently paged from the wrong position.
  `SeekTokenException` derives from `InvalidOperationException`, and tokens
  issued by earlier versions (no fingerprint) are still accepted.
- A token value that can't be converted to its column type now throws
  `SeekTokenException` (`InvalidValue`) instead of the converter's raw
  `FormatException`.
- **PostgreSQL:** a sort that includes a nullable column no longer uses the
  row-value comparison `(a, b) > (@a, @b)` — it can't be made null-aware — and
  goes through the fallback (`UNION ALL` by default, see **Performance**) instead. A
  reference-typed column counts as nullable unless annotated non-null
  (`string Name` under `<Nullable>enable</Nullable>`); in projects without
  nullable annotations every `string` sort column takes the fallback path.
- Tokens are ~25 characters longer (the `Sort` fingerprint), plus the expiry
  when `TokenLifetime` is set. `SeekData` gained `Sort`, `ExpiresAt`, and the
  non-serialized `NullOrdering`; a custom `ISeekSerializer` should round-trip
  the first two (required for `TokenLifetime` to work).
- **Source-breaking for custom implementations only:** `ISeekBuilder<T>`,
  `ISeekBuilder<T, TResult>`, and the `ISeekMongoBuilder` interfaces gained
  `WithTotalCount`, `ToPagesAsync`, and `ToItemsAsync`. Code that merely *uses*
  the builders is unaffected.

- `resultPropertyName` is now only needed for those untraceable cases (and for
  Mongo's aggregate/find origins, which build pipelines outside LINQ). Existing
  calls that pass it keep working unchanged, and a same-named `TResult` property
  is still preferred over tracing, so no existing cursor changes shape.
- **Dependencies** raised to the latest patch of each major line — these are the
  new *minimum* versions the packages require:

  | Target | EF Core | Microsoft.Extensions.* |
  |---|---|---|
  | `net8.0` | 8.0.16 → 8.0.34 | 8.0.2 (unchanged) |
  | `net9.0` | 9.0.6 → 9.0.23 | 9.0.6 → 9.0.23 |
  | `net10.0` | 10.0.6 → 10.0.15 | 10.0.6 → 10.0.15 |
  | `net11.0` | 11.0.0 (new) | 11.0.0 (new) |
  | `netstandard2.1` | 5.0.17 (unchanged) | 5.0.0 (unchanged) |

  `netstandard2.1` also moves `System.Text.Json` 6.0.7 → 6.0.11 (the patched
  release — the `NU1903` suppression is gone) and `System.Collections.Immutable`
  6.0.0 → 6.0.1. **SeekKit.MongoDB** now requires `MongoDB.Driver` 3.14.0
  (was 3.10.0).
- **SeekKit.MongoDB:** with `MongoDB.Driver` 3.14, paginating a LINQ `.Concat()`
  of two collections works (older drivers threw on `Concat` + `OrderBy` +
  `Take`); `.Union()` keeps working as before.

## [2.3.0] - 2026-08-14

All three packages release together at 2.3.0.

### Fixed

- **SeekKit.Core:** `OrderBy`/`OrderByDescending` threw `System.ArgumentException:
  Expression 'x' must be a property access` when sorting a scalar sequence (e.g.
  `IQueryable<int>`) by the element itself (`x => x`) instead of a property —
  common when pre-filtering to a set of ids before joining. Identity selectors are
  now supported for the plain (non-`Select()`) pagination path.

### Added

- **SeekKit.Core:** combining an identity sort selector (`x => x`) with
  `Select()` (EF) / the queryable `Select()` origin (Mongo) now auto-detects
  which `TResult` property holds the equivalent post-projection value, for two
  common shapes: `ids.Join(entities, x => x, e => e.Id, (_, e) => new TResult
  { Id = e.Id, ... })` and `ids.Select(id => new TResult { Id = id, ... })`.
  Detection runs the transformer once against an empty in-memory queryable to
  inspect the composed expression tree — no query executes, so this adds no
  extra round trip.
- **SeekKit.EntityFramework / SeekKit.MongoDB:** `OrderBy`/`OrderByDescending`
  gained an optional `resultPropertyName` parameter as a fallback for when
  auto-detection can't recognize the shape (nested joins, a renamed/computed
  member, or Mongo's aggregate/find origins, which build pipelines outside
  LINQ's expression trees and so can't be auto-detected at all). Attempting an
  identity + `Select()` combination that's undetectable and has no
  `resultPropertyName` now throws a targeted `InvalidOperationException` naming
  the fix, instead of a confusing "no public property '$self'" error.

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
