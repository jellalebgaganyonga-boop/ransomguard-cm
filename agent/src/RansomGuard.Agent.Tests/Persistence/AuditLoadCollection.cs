namespace RansomGuard.Agent.Tests.Persistence;

/// <summary>
/// The audit load tests (thousands of entries chained, SQLCipher, real watchers) run alone,
/// after the parallel tests: they saturate the CPU on purpose, and would otherwise make the
/// timing tests of unrelated classes fail.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AuditLoadCollection
{
    public const string Name = "Audit load (runs alone)";
}
