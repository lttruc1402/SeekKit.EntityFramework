/*
    Indexes for GET /products/sorted — one per sortable field.

    Keyset pagination is only O(page size) when the ORDER BY can be read straight off an
    index. seed-products.sql creates the one for the default sort (CreatedAt DESC, Id);
    this script adds the ones for ?sort=price:asc and ?sort=name:asc. Each ends in Id, the
    tie-breaker SeekSortMap appends to every sort.

    On the full 5-billion-row table each index takes a long time and a lot of disk to
    build — lower @TargetRows in seed-products.sql first if you only want a quick demo.

    Run with:
        sqlcmd -S localhost,1433 -U sa -P "<password>" -C -i sort-indexes.sql
*/
USE SeekKitDemo;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_Price_Id' AND object_id = OBJECT_ID(N'dbo.Products'))
    CREATE NONCLUSTERED INDEX IX_Products_Price_Id
        ON dbo.Products (Price ASC, Id ASC)
        WITH (ONLINE = OFF, SORT_IN_TEMPDB = ON, DATA_COMPRESSION = PAGE);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_Name_Id' AND object_id = OBJECT_ID(N'dbo.Products'))
    CREATE NONCLUSTERED INDEX IX_Products_Name_Id
        ON dbo.Products (Name ASC, Id ASC)
        WITH (ONLINE = OFF, SORT_IN_TEMPDB = ON, DATA_COMPRESSION = PAGE);
GO

-- A B-tree index serves both directions, so the same two indexes cover
-- ?sort=price:desc and ?sort=name:desc (where the tie-breaker flips to id:desc as well).
