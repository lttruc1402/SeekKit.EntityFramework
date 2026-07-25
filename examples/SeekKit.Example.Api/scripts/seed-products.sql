/* ============================================================================
   SeekKit demo — SQL Server seed script

   Creates the SeekKitDemo database, the Products table (schema matches the
   example API's ShopDbContext), and bulk-generates rows in 1M-row batches.

   !! 5,000,000,000 rows is a stress-test scale:
      - needs roughly 350-450 GB of disk for data + indexes
      - takes many hours, even on fast NVMe storage
      For a quick demo, lower @TargetRows below (e.g. 5,000,000).

   The script is RESUMABLE: it continues from MAX(Id), so you can stop it
   (Ctrl+C) and run it again later — or run it once with a small target and
   raise the target afterwards.

   Run inside the docker compose SQL Server container:
     docker exec -it seekkit-sqlserver /opt/mssql-tools18/bin/sqlcmd \
       -S localhost -U sa -P 'SeekKit!Passw0rd' -C -i /scripts/seed-products.sql
   ========================================================================== */

IF DB_ID(N'SeekKitDemo') IS NULL
    CREATE DATABASE SeekKitDemo;
GO

-- Minimal logging + no log-growth pain during the bulk load
ALTER DATABASE SeekKitDemo SET RECOVERY SIMPLE;
GO

USE SeekKitDemo;
GO

-- A handful of categories, looked up by /products/projected to demonstrate
-- ISeekBuilder<T>.Select<TResult> (push-down projection/join).
IF OBJECT_ID(N'dbo.Categories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Categories
    (
        Id   INT           NOT NULL IDENTITY(1, 1),
        Name NVARCHAR(64)  NOT NULL,
        CONSTRAINT PK_Categories PRIMARY KEY CLUSTERED (Id)
    );

    INSERT INTO dbo.Categories (Name)
    VALUES (N'Electronics'), (N'Home & Kitchen'), (N'Books'), (N'Toys'), (N'Sporting Goods');
END
GO

IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Products
    (
        Id         BIGINT         NOT NULL,
        Name       NVARCHAR(64)   NOT NULL,
        Price      DECIMAL(18, 2) NOT NULL,
        CreatedAt  DATETIME2(0)   NOT NULL,
        IsActive   BIT            NOT NULL,
        CategoryId INT            NOT NULL,
        CONSTRAINT PK_Products PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Products_Categories FOREIGN KEY (CategoryId)
            REFERENCES dbo.Categories (Id)
    );

    -- Composite index matching the API's sort: OrderByDescending(CreatedAt).OrderBy(Id)
    -- This is what makes keyset pagination O(page size) instead of O(offset).
    CREATE NONCLUSTERED INDEX IX_Products_CreatedAt_Id
        ON dbo.Products (CreatedAt DESC, Id ASC);
END
GO

SET NOCOUNT ON;

DECLARE @TargetRows BIGINT = 5000000000;   -- 5 billion. Lower this for a quick demo.
DECLARE @BatchSize  BIGINT = 1000000;      -- rows per INSERT batch

DECLARE @Existing BIGINT = ISNULL((SELECT MAX(Id) FROM dbo.Products), 0);
DECLARE @Start    DATETIME2 = SYSUTCDATETIME();

RAISERROR(N'Starting at %I64d existing rows, target %I64d.', 0, 1, @Existing, @TargetRows) WITH NOWAIT;

WHILE @Existing < @TargetRows
BEGIN
    DECLARE @ToInsert BIGINT =
        CASE WHEN @TargetRows - @Existing < @BatchSize
             THEN @TargetRows - @Existing
             ELSE @BatchSize END;

    -- Tally: 10^6 rows from a 6-way cross join of 10 values (no I/O needed)
    ;WITH Ten(n) AS (SELECT v FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) t(v)),
    Tally(rn) AS
    (
        SELECT TOP (@ToInsert) ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
        FROM Ten a CROSS JOIN Ten b CROSS JOIN Ten c
             CROSS JOIN Ten d CROSS JOIN Ten e CROSS JOIN Ten f
    )
    INSERT INTO dbo.Products WITH (TABLOCK) (Id, Name, Price, CreatedAt, IsActive, CategoryId)
    SELECT
        @Existing + rn,
        CONCAT(N'Product ', @Existing + rn),
        CAST((ABS(CHECKSUM(NEWID())) % 999900 + 100) / 100.0 AS DECIMAL(18, 2)),
        -- Spread CreatedAt over ~10 years (2015-2025), one second per step, wrapping
        DATEADD(SECOND, (@Existing + rn) % 315360000, '2015-01-01'),
        CASE WHEN (@Existing + rn) % 10 = 0 THEN 0 ELSE 1 END,
        (@Existing + rn) % 5 + 1   -- cycle through the 5 seeded categories
    FROM Tally;

    SET @Existing += @ToInsert;

    IF @Existing % 10000000 = 0
    BEGIN
        DECLARE @ElapsedMin INT = DATEDIFF(MINUTE, @Start, SYSUTCDATETIME());
        RAISERROR(N'  %I64d rows done (%d min elapsed)...', 0, 1, @Existing, @ElapsedMin) WITH NOWAIT;
        CHECKPOINT;   -- let the log truncate under SIMPLE recovery
    END
END

RAISERROR(N'Seed complete: %I64d rows.', 0, 1, @Existing) WITH NOWAIT;
GO
