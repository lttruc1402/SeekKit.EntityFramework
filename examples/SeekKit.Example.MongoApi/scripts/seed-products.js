/* ============================================================================
   SeekKit MongoDB demo — seed script (run with mongosh)

   Bulk-generates products in 10k-document batches. RESUMABLE: it continues
   from the current document count, so you can stop (Ctrl+C) and rerun.

   Adjust TARGET_DOCS below. 5,000,000 documents take a few minutes and a few
   GB of disk; scale up if you want a heavier benchmark.

   Run inside the docker compose Mongo container:
     docker exec -it seekkit-mongo mongosh /scripts/seed-products.js
   ========================================================================== */

const TARGET_DOCS = 5_000_000;   // lower for a quick demo, raise for stress tests
const BATCH_SIZE  = 10_000;

const dbo = db.getSiblingDB("seekkit_demo");
const col = dbo.getCollection("products");

// A smaller "archive" collection, used by the /products/union endpoint to
// demonstrate paginating across two collections as one stream. ~10% of TARGET.
const archive = dbo.getCollection("products_archive");
const TARGET_ARCHIVE = Math.floor(TARGET_DOCS / 10);

// A handful of categories, looked up by /products/projected to demonstrate
// ISeekMongoAggregateBuilder<T>.Select<TResult> (push-down $lookup).
const categories = dbo.getCollection("categories");
if (categories.estimatedDocumentCount() === 0) {
  categories.insertMany(
    ["Electronics", "Home & Kitchen", "Books", "Toys", "Sporting Goods"]
      .map((Name) => ({ Name }))
  );
}
const categoryIds = categories.find().toArray().map((c) => c._id);

// Compound index matching the API's sort: OrderByDescending(CreatedAt).OrderBy(Id).
// This is what makes keyset pagination O(page size) instead of O(offset).
col.createIndex({ CreatedAt: -1, _id: 1 });
archive.createIndex({ CreatedAt: -1, _id: 1 });

const baseTime = new Date("2015-01-01T00:00:00Z").getTime();
const existing = col.estimatedDocumentCount();
print(`Starting at ~${existing} existing docs, target ${TARGET_DOCS}.`);

let inserted = existing;
const start = Date.now();

while (inserted < TARGET_DOCS) {
  const n = Math.min(BATCH_SIZE, TARGET_DOCS - inserted);
  const batch = new Array(n);

  for (let i = 0; i < n; i++) {
    const seq = inserted + i;
    batch[i] = {
      Name:       `Product ${seq + 1}`,
      Price:      Math.round(Math.random() * 999_900 + 100) / 100,
      // Spread CreatedAt over ~10 years, one second per step, wrapping
      CreatedAt:  new Date(baseTime + (seq % 315_360_000) * 1000),
      IsActive:   seq % 10 !== 0,
      CategoryId: categoryIds[seq % categoryIds.length],
    };
  }

  col.insertMany(batch, { ordered: false });
  inserted += n;

  if (inserted % 100_000 === 0) {
    const mins = Math.round((Date.now() - start) / 60000);
    print(`  ${inserted} docs done (${mins} min elapsed)...`);
  }
}

print(`Seed complete: ${inserted} docs in 'products'.`);

// Seed the archive collection (older CreatedAt so it interleaves under the union)
let arch = archive.estimatedDocumentCount();
const archiveBase = new Date("2010-01-01T00:00:00Z").getTime();
while (arch < TARGET_ARCHIVE) {
  const n = Math.min(BATCH_SIZE, TARGET_ARCHIVE - arch);
  const batch = new Array(n);
  for (let i = 0; i < n; i++) {
    const seq = arch + i;
    batch[i] = {
      Name:      `Archived ${seq + 1}`,
      Price:     Math.round(Math.random() * 999_900 + 100) / 100,
      CreatedAt: new Date(archiveBase + (seq % 157_680_000) * 1000),
      IsActive:  false,
    };
  }
  archive.insertMany(batch, { ordered: false });
  arch += n;
}
print(`Seed complete: ${arch} docs in 'products_archive'.`);
