using Xunit;

// One shared MongoDB container for the whole assembly; run test classes
// sequentially so they don't contend on the container or resources.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
