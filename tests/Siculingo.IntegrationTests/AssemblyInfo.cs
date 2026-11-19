using Xunit;

// LocalDB Express shares a single local instance across integration test classes;
// running test classes sequentially avoids concurrent CREATE/DROP DATABASE contention
// while preserving full intra-test HTTP/SQL concurrency (25 active users, 100 readers, race conditions).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
