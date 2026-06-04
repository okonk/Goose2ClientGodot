using Xunit;

// All tests read shared .adf files from the Illutia data directory.
// AdfFile opens them with exclusive access, so parallel test execution
// causes IOException. Disable parallelization for the entire assembly.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
