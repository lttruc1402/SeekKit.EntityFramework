# Contributing to SeekKit.EntityFramework

Thanks for your interest in contributing! Issues and pull requests are welcome.

## Getting started

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
(the library multi-targets netstandard2.1 / net8.0 / net9.0 / net10.0, all built by the .NET 10 SDK).

```bash
git clone https://github.com/lttruc1402/SeekKit.EntityFramework.git
cd SeekKit.EntityFramework
dotnet test SeekKit.EntityFramework.slnx
```

Tests use in-memory SQLite — no database setup required.

## Reporting bugs

Open an issue with:

- The database provider and EF Core version you're using
- The sort configuration (`OrderBy`/`OrderByDescending` columns and types)
- The configured strategy (`DatabaseStrategy.ForXxx(...)`)
- Expected vs actual behavior — ideally with the generated SQL
  (enable `Microsoft.EntityFrameworkCore.Database.Command` logging)

## Pull requests

1. Fork and create a branch from `master`
2. Make your change, matching the existing code style
3. Add or update tests — every bug fix needs a regression test
4. Run `dotnet test SeekKit.EntityFramework.slnx` and make sure everything passes
5. Open a PR describing **what** changed and **why**

Notes:

- Public API changes should include XML doc comments
- Package versions are managed centrally in `Directory.Packages.props`
- New query strategies should implement `ISeekFilterStrategy` and come with
  integration tests in `SeekKit.EntityFramework.Tests/Integration`

## Running the example

An end-to-end demo (API + SQL Server + seed script) lives in
[`examples/SeekKit.Example.Api`](examples/SeekKit.Example.Api) — see its README.
