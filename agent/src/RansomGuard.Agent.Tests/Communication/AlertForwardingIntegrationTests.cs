using System.Net;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Service;
using Shouldly;

namespace RansomGuard.Agent.Tests.Communication;

/// <summary>
/// End-to-end integration tests for the alert forwarding pipeline.
/// Verifies: AlertMapper → Channel → AlertForwardingService → IGridApiClient (mocked).
/// Uses real SQLite in-memory for PendingAlertUpload persistence.
/// </summary>
public sealed class AlertForwardingIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IGridApiClient> _gridClientMock;
    private readonly AlertForwardingService _service;
    private readonly List<AlertIngestRequest> _receivedAlerts = [];
    private readonly object _lock = new();

    public AlertForwardingIntegrationTests()
    {
        _gridClientMock = new Mock<IGridApiClient>();
        _gridClientMock.Setup(c => c.IsEnrolled).Returns(true);

        // Default: successful sends
        _gridClientMock
            .Setup(c => c.SendAlertAsync(It.IsAny<AlertIngestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AlertIngestRequest req, CancellationToken _) =>
            {
                lock (_lock) { _receivedAlerts.Add(req); }
                return new AlertIngestResponse
                {
                    AlertId = Guid.NewGuid().ToString(),
                    Status = "created",
                    IngestedAt = DateTime.UtcNow,
                };
            });

        // Build DI container with real SQLite
        var dbName = $"file:alertfwd_{Guid.NewGuid()}?mode=memory&cache=shared";
        var services = new ServiceCollection();
        services.AddDbContext<AgentDbContext>(opt => opt.UseSqlite($"Data Source={dbName}"));
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        _serviceProvider = services.BuildServiceProvider();

        // Ensure DB schema is created
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        db.Database.EnsureCreated();

        // Build EnrollmentService with pre-signaled enrollment via reflection
        var enrollmentService = BuildEnrolledEnrollmentService();

        _service = new AlertForwardingService(
            _gridClientMock.Object,
            enrollmentService,
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AlertForwardingService>.Instance);
    }

    public void Dispose() => _serviceProvider.Dispose();

    // ===== Test 1: Full pipeline — CanaryAlert → GRID =====
    [Fact]
    public async Task CanaryAlert_FlowsThrough_Pipeline_To_Grid()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serviceTask = RunServiceAsync(cts.Token);

        var canaryAlert = new CanaryAlert
        {
            CanaryId = Guid.NewGuid(),
            CanaryPath = @"C:\Users\Desktop\0001_dossier_patient.docx",
            AlertType = CanaryAlertType.CanaryModified,
            Severity = AlertSeverity.Critical,
            OffendingProcessId = 1234,
            OffendingProcessName = "ransomware.exe",
            OffendingProcessPath = @"C:\temp\ransomware.exe",
        };

        var request = AlertMapper.FromCanaryAlert(canaryAlert);
        _service.TryEnqueue(request).ShouldBeTrue();

        await WaitForAlertsAsync(1, cts.Token);
        await StopServiceAsync(cts);
        await IgnoreCancellation(serviceTask);

        _receivedAlerts.Count.ShouldBe(1);
        _receivedAlerts[0].AlertType.ShouldBe("SentinelCanaryModified");
        _receivedAlerts[0].Severity.ShouldBe("Critical");
        _receivedAlerts[0].MitreTechniqueId.ShouldBe("T1486");
        _receivedAlerts[0].ClientMessageId.Length.ShouldBe(64);
        _service.TotalForwarded.ShouldBe(1);
    }

    // ===== Test 2: Multiple alert types forwarded in order =====
    [Fact]
    public async Task Multiple_AlertTypes_Forwarded_In_Order()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serviceTask = RunServiceAsync(cts.Token);

        _service.TryEnqueue(AlertMapper.FromCanaryAlert(new CanaryAlert
        {
            CanaryId = Guid.NewGuid(),
            CanaryPath = "test_canary",
            AlertType = CanaryAlertType.CanaryDeleted,
            Severity = AlertSeverity.High,
        }));

        _service.TryEnqueue(AlertMapper.FromEntropyAlert(new EntropyAlert
        {
            FilePath = @"C:\docs\report.docx",
            RuleId = 1, RuleName = "AbsoluteHigh", Severity = "Critical",
            BaselineEntropy = 4.2, CurrentEntropy = 7.9, Delta = 3.7,
        })!);

        _service.TryEnqueue(AlertMapper.FromUsbAlert(new UsbAlert
        {
            UsbScanResultId = Guid.NewGuid(),
            Title = "USB GUARD: Critical — Bootable USB",
            Description = "Bootable USB blocked",
            Severity = "Critical", ActionTaken = "BlockAndEject",
        }));

        await WaitForAlertsAsync(3, cts.Token);
        await StopServiceAsync(cts);
        await IgnoreCancellation(serviceTask);

        _receivedAlerts.Count.ShouldBe(3);
        _receivedAlerts[0].AlertType.ShouldBe("SentinelCanaryDeleted");
        _receivedAlerts[1].AlertType.ShouldBe("EntropyAbsoluteHigh");
        _receivedAlerts[2].AlertType.ShouldBe("UsbGuard");
        _service.TotalForwarded.ShouldBe(3);
    }

    // ===== Test 3: Network failure persists to PendingAlertUpload =====
    [Fact]
    public async Task Network_Failure_Persists_To_PendingAlertUpload()
    {
        _gridClientMock
            .Setup(c => c.SendAlertAsync(It.IsAny<AlertIngestRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serviceTask = RunServiceAsync(cts.Token);

        var request = AlertMapper.FromCanaryAlert(new CanaryAlert
        {
            CanaryId = Guid.NewGuid(),
            CanaryPath = "test",
            AlertType = CanaryAlertType.CanaryModified,
            Severity = AlertSeverity.Critical,
        });

        _service.TryEnqueue(request);
        await Task.Delay(2000, cts.Token);

        await StopServiceAsync(cts);
        await IgnoreCancellation(serviceTask);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var pending = await db.PendingAlertUploads.FirstOrDefaultAsync();

        pending.ShouldNotBeNull();
        pending.ClientMessageId.ShouldBe(request.ClientMessageId);
        pending.Status.ShouldBe(PendingUploadStatus.Pending);
        pending.AttemptCount.ShouldBe(1);
        pending.LastErrorMessage!.ShouldContain("Connection refused");
    }

    // ===== Test 4: Validation error (422) marks FailedValidation — no retry =====
    [Fact]
    public async Task Validation_Error_Marks_FailedValidation_No_Retry()
    {
        _gridClientMock
            .Setup(c => c.SendAlertAsync(It.IsAny<AlertIngestRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("422 Unprocessable", null, HttpStatusCode.UnprocessableEntity));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serviceTask = RunServiceAsync(cts.Token);

        var request = AlertMapper.FromCanaryAlert(new CanaryAlert
        {
            CanaryId = Guid.NewGuid(),
            CanaryPath = "test",
            AlertType = CanaryAlertType.CanaryModified,
        });

        _service.TryEnqueue(request);
        await Task.Delay(2000, cts.Token);

        await StopServiceAsync(cts);
        await IgnoreCancellation(serviceTask);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var pending = await db.PendingAlertUploads.FirstOrDefaultAsync();

        pending.ShouldNotBeNull();
        pending.Status.ShouldBe(PendingUploadStatus.FailedValidation);
        _service.TotalFailedValidation.ShouldBe(1);
    }

    // ===== Test 5: Suppressed EntropyAlert is never forwarded =====
    [Fact]
    public void Suppressed_EntropyAlert_Never_Forwarded()
    {
        var mapped = AlertMapper.FromEntropyAlert(new EntropyAlert
        {
            FilePath = @"C:\file.zip",
            RuleId = 4, RuleName = "ExtensionWhitelist", Severity = "Suppressed",
            BaselineEntropy = 0, CurrentEntropy = 7.9, Delta = 0,
        });

        mapped.ShouldBeNull();
    }

    // ===== Test 6: ExfilFinding dedup — same minute produces same client_message_id =====
    [Fact]
    public async Task ExfilFinding_Same_Minute_Sent_With_Same_ClientMessageId()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serviceTask = RunServiceAsync(cts.Token);

        var baseTime = new DateTime(2026, 7, 1, 14, 30, 0, DateTimeKind.Utc);
        var req1 = AlertMapper.FromExfilFinding(new RansomGuard.Agent.Core.Detection.ExfilWatch.Rules.ExfilFinding
        {
            RuleName = "VolumeAnomaly",
            Severity = RansomGuard.Agent.Core.Detection.ExfilWatch.Rules.ExfilSeverity.High,
            Description = "desc1", ProcessId = 100, ProcessName = "proc",
            Destination = "10.0.0.1", BytesTransferred = 1000, MitreId = "T1041",
            DetectedAt = baseTime.AddSeconds(10),
        });

        var req2 = AlertMapper.FromExfilFinding(new RansomGuard.Agent.Core.Detection.ExfilWatch.Rules.ExfilFinding
        {
            RuleName = "VolumeAnomaly",
            Severity = RansomGuard.Agent.Core.Detection.ExfilWatch.Rules.ExfilSeverity.High,
            Description = "desc2", ProcessId = 100, ProcessName = "proc",
            Destination = "10.0.0.1", BytesTransferred = 2000, MitreId = "T1041",
            DetectedAt = baseTime.AddSeconds(45),
        });

        req1.ClientMessageId.ShouldBe(req2.ClientMessageId, "Same minute should produce same ID");

        _service.TryEnqueue(req1);
        _service.TryEnqueue(req2);

        await WaitForAlertsAsync(2, cts.Token);
        await StopServiceAsync(cts);
        await IgnoreCancellation(serviceTask);

        _receivedAlerts.Count.ShouldBe(2);
        _receivedAlerts[0].ClientMessageId.ShouldBe(_receivedAlerts[1].ClientMessageId);
    }

    // ===== Test 7: IndicatorRemoval flows through pipeline =====
    [Fact]
    public async Task IndicatorRemoval_FlowsThrough_Pipeline()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serviceTask = RunServiceAsync(cts.Token);

        var request = AlertMapper.FromIndicatorRemovalEvent(new IndicatorRemovalEvent
        {
            EventType = IndicatorRemovalType.EventLogClearing,
            MitreTechniqueId = "T1070.001",
            ProcessId = 999, ProcessName = "wevtutil.exe",
            CommandLine = "wevtutil cl Security",
            TargetResource = "Security", Severity = "Critical",
            Description = "Security event log cleared", ActionTaken = "Alert",
        });

        _service.TryEnqueue(request);

        await WaitForAlertsAsync(1, cts.Token);
        await StopServiceAsync(cts);
        await IgnoreCancellation(serviceTask);

        _receivedAlerts.Count.ShouldBe(1);
        _receivedAlerts[0].AlertType.ShouldBe("IndicatorRemovalEventLogClearing");
        _receivedAlerts[0].MitreTechniqueId.ShouldBe("T1070.001");
    }

    // ===== Test 8: TryEnqueue returns true when queue has capacity =====
    [Fact]
    public void TryEnqueue_Returns_True_When_Queue_Has_Capacity()
    {
        var request = AlertMapper.FromCanaryAlert(new CanaryAlert
        {
            CanaryId = Guid.NewGuid(),
            CanaryPath = "test",
            AlertType = CanaryAlertType.CanaryModified,
        });

        _service.TryEnqueue(request).ShouldBeTrue();
    }

    // ===== Helpers =====

    private Task RunServiceAsync(CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            try { await ((IHostedService)_service).StartAsync(ct); }
            catch (OperationCanceledException) { }
        }, CancellationToken.None);
    }

    private static async Task StopServiceAsync(CancellationTokenSource cts)
    {
        await cts.CancelAsync();
        await Task.Delay(100); // Let cleanup finish
    }

    private async Task WaitForAlertsAsync(int expected, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            lock (_lock)
            {
                if (_receivedAlerts.Count >= expected) return;
            }
            await Task.Delay(50, ct);
        }
    }

    private static async Task IgnoreCancellation(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
    }

    private static EnrollmentService BuildEnrolledEnrollmentService()
    {
        // Build minimal valid AgentConfiguration for EnrollmentStateManager + EnrollmentService
        var config = new AgentConfiguration
        {
            Identity = new AgentIdentityOptions { Id = "test-agent", Hostname = "TEST", Version = "1.0.0", Environment = "Test" },
            Server = new ServerOptions { BaseUrl = "https://localhost" },
            Detection = new DetectionOptions { WatchPaths = [] },
            Database = new DatabaseOptions { ConnectionString = $"Data Source={Path.GetTempPath()}ransomguard_test_{Guid.NewGuid()}.db" },
            Logging = new LoggingOptions { MinimumLevel = "Warning", LogFilePath = "test.log" },
        };
        var configOptions = Options.Create(config);

        // Build a real GridApiClient (won't be used — AlertForwardingService uses IGridApiClient mock)
        var mockHttpFactory = new Mock<IHttpClientFactory>();
        mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var gridClient = new GridApiClient(mockHttpFactory.Object, configOptions, NullLogger<GridApiClient>.Instance);

        // Build EnrollmentStateManager (won't actually write files)
        var stateManager = new EnrollmentStateManager(configOptions, NullLogger<EnrollmentStateManager>.Instance);

        var enrollmentService = new EnrollmentService(gridClient, stateManager, configOptions, NullLogger<EnrollmentService>.Instance);

        // Signal enrollment complete via reflection
        var signalField = typeof(EnrollmentService).GetField("_enrolledSignal", BindingFlags.NonPublic | BindingFlags.Instance);
        var tcs = (TaskCompletionSource?)signalField?.GetValue(enrollmentService);
        tcs?.TrySetResult();

        return enrollmentService;
    }
}
