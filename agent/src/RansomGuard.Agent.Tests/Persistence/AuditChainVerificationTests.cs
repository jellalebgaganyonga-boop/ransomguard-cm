using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.Persistence;

/// <summary>
/// The verdict of --verify-audit-log: an empty chain is INCONCLUSIVE, never PASS.
/// </summary>
public sealed class AuditChainVerificationTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly AuditLogRepository _repository;

    public AuditChainVerificationTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _repository = new AuditLogRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task Empty_chain_is_inconclusive_never_pass()
    {
        var (verdict, entries) = await AuditChainVerification.VerifyAsync(_context, _repository);

        verdict.ShouldBe(AuditChainVerdict.Inconclusive);
        entries.ShouldBe(0);
        AuditChainVerification.InconclusiveExitCode.ShouldBe(3);
    }

    [Fact]
    public async Task Intact_chain_passes()
    {
        await _repository.AppendAsync("AgentStarted", "first");
        await _repository.AppendAsync("CanaryAlert", "second");

        var (verdict, entries) = await AuditChainVerification.VerifyAsync(_context, _repository);

        verdict.ShouldBe(AuditChainVerdict.Pass);
        entries.ShouldBe(2);
    }

    [Fact]
    public async Task Tampered_chain_fails()
    {
        await _repository.AppendAsync("AgentStarted", "first");
        await _repository.AppendAsync("CanaryAlert", "second");
        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE AuditLogs SET Details = 'TAMPERED' WHERE Sequence = 1");

        var (verdict, _) = await AuditChainVerification.VerifyAsync(_context, _repository);

        verdict.ShouldBe(AuditChainVerdict.Fail);
    }
}
