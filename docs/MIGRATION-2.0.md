# Migrating from SeekKit 1.x to 2.0

SeekKit 2.0 splits the library into three packages so the same pagination
contracts can be shared across database providers:

| Package | What it contains |
|---------|------------------|
| `SeekKit.Core` | Shared contracts (`SeekResult<T>`, `SeekRequest`, ...), token serialization, type converters — **installed automatically as a dependency** |
| `SeekKit.EntityFramework` | The EF Core provider (everything you used in 1.x) |
| `SeekKit.MongoDB` | New: the MongoDB provider |

**The good news:** no API shapes changed. Every class, method, and option from
1.x works exactly the same — only namespaces moved. For most projects the
whole migration is a find-and-replace of `using` directives.

## 1. Update the package

```bash
dotnet add package SeekKit.EntityFramework --version 2.0.0
```

`SeekKit.Core` comes along automatically — you don't need to install it.

## 2. Update your `using` directives

Shared types moved from `SeekKit.EntityFramework.*` into `SeekKit.Core.*`:

| Type | 1.x namespace | 2.0 namespace |
|------|---------------|---------------|
| `SeekResult<T>`, `SeekRequest`, `SeekData`, `SeekDirection`, `PageMetadata`, `ISortColumn<T>` | `SeekKit.EntityFramework.Models` | `SeekKit.Core.Models` |
| `ISeekSerializer`, `TypeConverter<T>`, `ITypeConverterRegistry`, `ISeekValueConverter`, `ISeekFilterStrategy`, `IPageSizeAware` | `SeekKit.EntityFramework.Core` | `SeekKit.Core` |
| `SeekKitConfiguration` | `SeekKit.EntityFramework` | `SeekKit.Core` |
| Built-in converters (`IntConverter`, `GuidConverter`, ...) | `SeekKit.EntityFramework.Converters` | `SeekKit.Core.Converters` |
| `OrLogicSeekStrategy` (now public) | *(internal in 1.x)* | `SeekKit.Core.Strategies` |

**Unchanged** — these stay where they were:

| Type | Namespace (same as 1.x) |
|------|-------------------------|
| `AddSeekKit`, `Extensions` | `SeekKit.EntityFramework` |
| `SeekKitOptions`, `DatabaseStrategy`, `DatabaseType`, strategy enums, `ISeekService`, `ISeekFactory` | `SeekKit.EntityFramework.Core` |
| `ISeekBuilder<T>` | `SeekKit.EntityFramework.Builders` |
| `IQueryableHelper` extensions | `SeekKit.EntityFramework.Helpers` |

### Find-and-replace cheat sheet

Apply these replacements across your solution (in this order):

```
using SeekKit.EntityFramework.Models;      →  using SeekKit.Core.Models;
using SeekKit.EntityFramework.Converters;  →  using SeekKit.Core.Converters;
```

Then, **only if** you reference `ISeekSerializer`, `TypeConverter<T>`,
`SeekKitConfiguration`, or `ISeekFilterStrategy` directly, add:

```csharp
using SeekKit.Core;
```

(Keep `using SeekKit.EntityFramework.Core;` too if you use `DatabaseStrategy`
or `SeekKitOptions` — those did not move.)

### Before / after

```csharp
// 1.x
using SeekKit.EntityFramework;
using SeekKit.EntityFramework.Core;
using SeekKit.EntityFramework.Models;

// 2.0
using SeekKit.Core.Models;            // SeekResult<T>, SeekRequest
using SeekKit.EntityFramework;        // AddSeekKit
using SeekKit.EntityFramework.Core;   // DatabaseStrategy, SeekKitOptions
```

Everything else — registration, queries, tokens — is identical:

```csharp
// Works unchanged in 2.0
builder.Services.AddSeekKit(options =>
{
    options.Strategy        = DatabaseStrategy.ForSqlServer();
    options.DefaultPageSize = 20;
});

var page = await seek.SeekAsync(
    db.Products.Where(p => p.IsActive),
    new SeekRequest { Token = token, PageSize = 20 },
    b => b.OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id));
```

## 3. Behavior change: malformed tokens

In 1.x, a malformed or truncated token could throw
`InvalidOperationException` at query time. In 2.0 it falls back to the first
page instead — consistent with the `ISeekSerializer` contract. If you relied
on catching that exception to detect bad tokens, check
`result.PreviousToken == null && result.HasPrevious == false` instead, or
enable HMAC signing to reject tampered tokens outright.

## 4. Tokens are compatible

The token format did not change. Tokens issued by 1.x continue to work with
2.0 (and vice versa) as long as you don't enable HMAC signing — enabling it
invalidates all previously issued unsigned tokens, which clients experience
as being sent back to the first page.

## 5. New in 2.0 (optional)

- **MongoDB provider** — `dotnet add package SeekKit.MongoDB`, then
  `AddSeekKitMongo()` and inject `ISeekMongoService`. See the
  [README](../README.md#mongodb--iseekmongoservice).
- **Both providers in one app** — `AddSeekKit` + `AddSeekKitMongo` can be
  registered together in any order; they share one converter registry and one
  token serializer.
- **`OrLogicSeekStrategy` is public** — usable via `WithStrategy(...)` or as
  a reference for custom `ISeekFilterStrategy` implementations.

## Checklist

- [ ] Bump `SeekKit.EntityFramework` to 2.0.0
- [ ] Replace `SeekKit.EntityFramework.Models` → `SeekKit.Core.Models` in usings
- [ ] Replace `SeekKit.EntityFramework.Converters` → `SeekKit.Core.Converters` (if used)
- [ ] Add `using SeekKit.Core;` where you touch serializers/converters/configuration
- [ ] Rebuild — compiler errors point at any using you missed
- [ ] If you caught exceptions from bad tokens, review section 3
