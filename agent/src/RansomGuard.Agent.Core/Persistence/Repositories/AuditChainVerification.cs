namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>Verdict of an audit chain verification.</summary>
public enum AuditChainVerdict
{
    /// <summary>Entries present, hash chain intact, signatures valid.</summary>
    Pass,

    /// <summary>Integrity check failed: the chain is broken or a signature is invalid.</summary>
    Fail,

    /// <summary>
    /// The chain is empty. A fresh install and a chain wiped by an attacker both produce zero
    /// entries, so locally nothing can be concluded.
    /// </summary>
    Inconclusive,
}

/// <summary>
/// Turns the state of the audit chain into a verdict. An empty chain is INCONCLUSIVE, never
/// PASS: printing PASS for both a fresh install and an erased chain made the verifier useless in
/// the only case where it matters. The verdict on an empty chain becomes conclusive only once
/// the GRID anchors the chain and the agent's count can be compared with what the server holds.
/// </summary>
public static class AuditChainVerification
{
    /// <summary>Exit code of <c>--verify-audit-log</c> for an inconclusive (empty) chain.</summary>
    public const int InconclusiveExitCode = 3;

    /// <summary>Verifies the chain behind <paramref name="repository"/>.</summary>
    public static async Task<(AuditChainVerdict Verdict, int Entries)> VerifyAsync(
        AgentDbContext context, IAuditLogRepository repository, CancellationToken cancellationToken = default)
    {
        int entries = context.AuditLogs.Count();
        if (entries == 0)
        {
            return (AuditChainVerdict.Inconclusive, 0);
        }

        bool intact = await repository.VerifyChainIntegrityAsync(cancellationToken).ConfigureAwait(false);
        return (intact ? AuditChainVerdict.Pass : AuditChainVerdict.Fail, entries);
    }
}
