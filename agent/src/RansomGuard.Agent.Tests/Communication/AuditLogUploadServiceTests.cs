using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Core.Security.Cryptography;
using RansomGuard.Agent.Service;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Communication;

/// <summary>
/// The workstation's audit chain reaches the GRID, in order, once each.
///
/// <para>
/// Nothing uploaded it until 2026-10-27: the client could, the GRID was listening and the
/// console had a page, and no code joined them. The owner ran a real detection, a real
/// suspension and a real Resume, and the Audit log page said "No entries found".
/// </para>
/// </summary>
public sealed class AuditLogUploadServiceTests : IDisposable
{
    private readonly MonitorHarness _h = new();
    private readonly Mock<IGridApiClient> _grid = new();
    private readonly List<AuditLogBatchRequest> _sent = [];

    public AuditLogUploadServiceTests()
    {
        _grid.SetupGet(g => g.IsEnrolled).Returns(true);
        Accept();
    }

    public void Dispose() => _h.Dispose();

    /// <summary>The GRID takes everything it is given.</summary>
    private void Accept() =>
        _grid.Setup(g => g.SendAuditLogBatchAsync(It.IsAny<AuditLogBatchRequest>(), It.IsAny<CancellationToken>()))
            .Callback((AuditLogBatchRequest r, CancellationToken _) => _sent.Add(r))
            .ReturnsAsync((AuditLogBatchRequest r, CancellationToken _) =>
                new AuditLogBatchResponse { AcceptedCount = r.Entries.Count, RejectedCount = 0 });

    private AuditLogUploadService Service() => new(
        _grid.Object, _h.ScopeFactory,
        new AuditLogSignerStub(), NullLogger<AuditLogUploadService>.Instance);

    private async Task RecordAsync(params string[] actions)
    {
        using IServiceScope scope = _h.Provider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        foreach (string action in actions)
        {
            await repo.AppendAsync(action, $"details of {action}");
        }
        await repo.ChainPendingAsync();
    }

    private async Task<int> WaitingAsync()
    {
        using IServiceScope scope = _h.Provider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        return (await repo.GetPendingUploadAsync(1000)).Count;
    }

    [Fact]
    public async Task The_chain_this_workstation_wrote_is_handed_to_the_GRID()
    {
        await RecordAsync("SentinelCanaryModified", "SentinelProcessSuspended", "SentinelProcessResumed");

        (await Service().UploadOnceAsync(CancellationToken.None)).ShouldBe(3);

        _sent.Count.ShouldBe(1);
        _sent[0].Entries.Select(e => e.SequenceNumber).ShouldBe(_sent[0].Entries.Select(e => e.SequenceNumber).OrderBy(s => s));
        _sent[0].Entries.Select(e => (string)e.Payload["action"].GetString()!)
            .ShouldBe(["SentinelCanaryModified", "SentinelProcessSuspended", "SentinelProcessResumed"]);
        // Every entry carries the timestamp the GRID's schema demands; without it the whole
        // batch is answered 422 before a signature is ever looked at.
        _sent[0].Entries.ShouldAllBe(e => e.Timestamp != default);
    }

    [Fact]
    public async Task An_entry_already_handed_over_is_not_sent_twice()
    {
        await RecordAsync("AgentStarted", "SentinelCanaryModified");

        (await Service().UploadOnceAsync(CancellationToken.None)).ShouldBe(2);
        (await WaitingAsync()).ShouldBe(0);

        // Nothing new happened, so nothing is sent.
        (await Service().UploadOnceAsync(CancellationToken.None)).ShouldBe(0);
        _sent.Count.ShouldBe(1);

        // And something new is sent on its own, not with the old entries again.
        await RecordAsync("SentinelProcessResumed");
        (await Service().UploadOnceAsync(CancellationToken.None)).ShouldBe(1);
        _sent[1].Entries.Single().Payload["action"].GetString().ShouldBe("SentinelProcessResumed");
    }

    [Fact]
    public async Task A_batch_the_GRID_refuses_stays_waiting_and_goes_again()
    {
        await RecordAsync("AgentStarted", "SentinelCanaryModified");

        _grid.Setup(g => g.SendAuditLogBatchAsync(It.IsAny<AuditLogBatchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditLogBatchResponse
            {
                AcceptedCount = 0,
                RejectedCount = 2,
                RejectedReasons = ["seq=1: Invalid Ed25519 signature"],
            });

        (await Service().UploadOnceAsync(CancellationToken.None)).ShouldBe(0);

        // Refused is not delivered. Marking it would leave the console permanently missing the
        // entries, with nothing on the workstation saying so.
        (await WaitingAsync()).ShouldBe(2);

        Accept();
        (await Service().UploadOnceAsync(CancellationToken.None)).ShouldBe(2);
        (await WaitingAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Only_what_the_GRID_accepted_is_marked_when_it_takes_part_of_a_batch()
    {
        await RecordAsync("A", "B", "C");

        _grid.Setup(g => g.SendAuditLogBatchAsync(It.IsAny<AuditLogBatchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditLogBatchResponse
            {
                AcceptedCount = 2, RejectedCount = 1, RejectedReasons = ["seq=3: sequence gap"],
            });

        (await Service().UploadOnceAsync(CancellationToken.None)).ShouldBe(2);

        // The GRID takes them in order and stops at the first it will not have, so the one left
        // is the last one -- and it is still waiting, not skipped.
        (await WaitingAsync()).ShouldBe(1);
    }

    /// <summary>
    /// A signer that does not need Windows DPAPI or a key on disk. What is under test here is
    /// which entries are sent and what happens to them afterwards; the bytes the signature
    /// covers are pinned separately, against Python's output, in AuditUploadPayloadTests.
    /// </summary>
    private sealed class AuditLogSignerStub : IAuditUploadSigner
    {
        public string KeyId => "0123456789abcdef0123456789abcdef";

        public string SignUpload(ReadOnlySpan<byte> canonicalJson) =>
            Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(canonicalJson.ToArray()))[..88];
    }
}
