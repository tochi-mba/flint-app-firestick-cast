using Xunit;

// Real mouse, keyboard and TV focus are shared resources even when app processes are separate.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
