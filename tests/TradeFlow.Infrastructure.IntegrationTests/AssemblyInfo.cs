using Xunit;

// Prevents concurrent Testcontainers initialization from saturating the Windows Docker named pipe (npipe://./pipe/docker_engine).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
