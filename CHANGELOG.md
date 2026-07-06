# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[1.0.0]: https://github.com/lttruc1402/SeekKit.EntityFramework/releases/tag/v1.0.0
