using Xunit;

// The interface language is process-wide state (CultureInfo defaults); the suites are small, so they run
// one at a time and a test that switches the language cannot disturb another one.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
