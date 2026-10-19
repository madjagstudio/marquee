namespace Marquee.App.Tests.TestSupport;

/// Strings.ResourceManager is one static for the whole assembly, so tests that swap it must not overlap any test that
/// reads a message. xUnit runs a collection with parallelization off by itself, after the parallel ones.
[CollectionDefinition(nameof(SwappedResources), DisableParallelization = true)]
public sealed class SwappedResourcesCollection;
