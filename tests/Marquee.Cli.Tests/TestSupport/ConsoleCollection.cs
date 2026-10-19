namespace Marquee.Cli.Tests.TestSupport;

/// The console and Strings.ResourceManager are one each for the whole process, so tests that capture or swap them
/// must not overlap. xUnit runs a collection with parallelization off by itself, after the parallel ones.
[CollectionDefinition(nameof(ConsoleCapture), DisableParallelization = true)]
public sealed class ConsoleCollection;
