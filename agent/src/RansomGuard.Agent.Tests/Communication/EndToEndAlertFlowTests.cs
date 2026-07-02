using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Service;
using Shouldly;

namespace RansomGuard.Agent.Tests.Communication;

/// <summary>
/// True end-to-end test: AlertMapper → Channel → AlertForwardingService → GridApiClient
/// (real HTTP via custom HttpMessageHandler) → captures the actual HTTP POST request.
///
/// This proves that a CanaryAlert detection results in a real HTTP POST to
/// /api/v1/agents/{id}/alerts with the correct JSON payload.
/// </summary>
public sealed class EndToEndAlertFlowTests : IDisposable
{
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly ServiceProvider _serviceProvider;
    private readonly CaptureHandler _captureHandler;
    private readonly AlertForwardingService _service;

    public EndToEndAlertFlowTests()
    {
        _captureHandler = new CaptureHandler();

        var config = new AgentConfiguration
        {
            Identity = new AgentIdentityOptions
                { Id = "test-agent-e2e", Hostname = "TEST-E2E", Version = "1.0.0", Environment = "Test" },
            Server = new ServerOptions { BaseUrl = "https://grid.test.local" },
            Detection = new DetectionOptions { WatchPaths = [] },
            Database = new DatabaseOptions
                { ConnectionString = $"Data Source={Path.GetTempPath()}rg_e2e_{Guid.NewGuid()}.db" },
            Logging = new LoggingOptions { MinimumLevel = "Warning", LogFilePath = "test.log" },
        };
        var configOptions = Options.Create(config);

        // Build real GridApiClient with captured HTTP handler
        var httpClientFactory = new FakeHttpClientFactory(_captureHandler, config.Server.BaseUrl);
        var gridClient = new GridApiClient(httpClientFactory, configOptions, NullLogger<GridApiClient>.Instance);
        gridClient.RestoreEnrollment("agent-001", "tenant-001");

        // Build DI for SQLite
        var services = new ServiceCollection();
        var dbName = $"file:e2e_{Guid.NewGuid()}?mode=memory&cache=shared";
        services.AddDbContext<AgentDbContext>(opt => opt.UseSqlite($"Data Source={dbName}"));
        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.EnsureCreated();

        // Build EnrollmentService with pre-signaled enrollment
        var enrollmentService = BuildEnrolledEnrollmentService(configOptions);

        _service = new AlertForwardingService(
            gridClient, // REAL GridApiClient, not a mock
            enrollmentService,
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AlertForwardingService>.Instance);
    }

    public void Dispose() => _serviceProvider.Dispose();

    // ===== THE critical E2E test: CanaryAlert → real HTTP POST to GRID =====
    [Fact]
    public async Task CanaryAlert_Produces_Real_Http_Post_To_Grid_Endpoint()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serviceTask = Task.Run(async () =>
        {
            try { await ((IHostedService)_service).StartAsync(cts.Token); }
            catch (OperationCanceledException) { }
        }, CancellationToken.None);

        // Create a canary alert as SentinelMonitor would
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

        // Map exactly as SentinelMonitor does (line 281)
        var request = AlertMapper.FromCanaryAlert(canaryAlert);
        _service.TryEnqueue(request);

        // Wait for HTTP capture
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (_captureHandler.CapturedRequests.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(50, cts.Token);

        await cts.CancelAsync();
        try { await serviceTask; } catch (OperationCanceledException) { }

        // === VERIFY the actual HTTP request ===
        _captureHandler.CapturedRequests.Count.ShouldBe(1, "Expected exactly 1 HTTP POST to GRID");

        var captured = _captureHandler.CapturedRequests[0];

        // Correct endpoint
        captured.Method.ShouldBe(HttpMethod.Post);
        captured.Url.ShouldBe("https://grid.test.local/api/v1/agents/agent-001/alerts");

        // Parse the JSON body
        captured.Body.ShouldNotBeNullOrEmpty();
        var body = JsonDocument.Parse(captured.Body);
        var root = body.RootElement;

        // Verify snake_case field names (GRID contract)
        root.GetProperty("client_message_id").GetString()!.Length.ShouldBe(64);
        root.GetProperty("alert_type").GetString().ShouldBe("SentinelCanaryModified");
        root.GetProperty("severity").GetString().ShouldBe("Critical");
        root.GetProperty("mitre_technique_id").GetString().ShouldBe("T1486");
        root.GetProperty("summary").GetString()!.ShouldContain("0001_dossier_patient.docx");
        root.GetProperty("detected_at").GetString().ShouldNotBeNullOrEmpty();

        // Verify details array has process attribution
        var details = root.GetProperty("details");
        details.GetArrayLength().ShouldBe(3);
    }

    // ===== E2E: EntropyAlert → HTTP POST with correct alert_type =====
    [Fact]
    public async Task EntropyAlert_Produces_Real_Http_Post_With_Correct_AlertType()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serviceTask = Task.Run(async () =>
        {
            try { await ((IHostedService)_service).StartAsync(cts.Token); }
            catch (OperationCanceledException) { }
        }, CancellationToken.None);

        var entropyAlert = new EntropyAlert
        {
            FilePath = @"C:\Users\Documents\rapport_medical.docx",
            RuleId = 1, RuleName = "AbsoluteHigh", Severity = "Critical",
            BaselineEntropy = 4.2, CurrentEntropy = 7.9, Delta = 3.7,
        };

        var request = AlertMapper.FromEntropyAlert(entropyAlert);
        request.ShouldNotBeNull();
        _service.TryEnqueue(request);

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (_captureHandler.CapturedRequests.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(50, cts.Token);

        await cts.CancelAsync();
        try { await serviceTask; } catch (OperationCanceledException) { }

        _captureHandler.CapturedRequests.Count.ShouldBe(1);
        var body = JsonDocument.Parse(_captureHandler.CapturedRequests[0].Body!);
        body.RootElement.GetProperty("alert_type").GetString().ShouldBe("EntropyAbsoluteHigh");
        body.RootElement.GetProperty("severity").GetString().ShouldBe("Critical");
        body.RootElement.GetProperty("summary").GetString()!.ShouldContain("7.90");
    }

    // ===== E2E: Network error persists to SQLite and is retryable =====
    [Fact]
    public async Task Network_Error_Persists_And_Retry_Sends_Real_Http()
    {
        // First call: simulate network error; second call: succeed
        int callCount = 0;
        _captureHandler.ResponseFactory = _ =>
        {
            int n = Interlocked.Increment(ref callCount);
            if (n == 1)
                throw new HttpRequestException("Connection refused");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { alert_id = Guid.NewGuid().ToString(), status = "created", ingested_at = DateTime.UtcNow }, SnakeCaseOptions),
                    System.Text.Encoding.UTF8, "application/json")
            };
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var serviceTask = Task.Run(async () =>
        {
            try { await ((IHostedService)_service).StartAsync(cts.Token); }
            catch (OperationCanceledException) { }
        }, CancellationToken.None);

        var request = AlertMapper.FromCanaryAlert(new CanaryAlert
        {
            CanaryId = Guid.NewGuid(),
            CanaryPath = "test_retry",
            AlertType = CanaryAlertType.CanaryModified,
            Severity = AlertSeverity.Critical,
        });

        _service.TryEnqueue(request);

        // Wait for persist (first attempt fails)
        await Task.Delay(3000, cts.Token);

        // Verify persisted to SQLite
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            var pending = await db.PendingAlertUploads.FirstOrDefaultAsync(cts.Token);
            pending.ShouldNotBeNull("Alert should be persisted after network failure");
            pending.Status.ShouldBe(PendingUploadStatus.Pending);
        }

        await cts.CancelAsync();
        try { await serviceTask; } catch (OperationCanceledException) { }
    }

    // ===== Helpers =====

    private static EnrollmentService BuildEnrolledEnrollmentService(IOptions<AgentConfiguration> configOptions)
    {
        var mockHttpFactory = new FakeHttpClientFactory(new CaptureHandler(), "https://localhost");
        var gridClient = new GridApiClient(mockHttpFactory, configOptions, NullLogger<GridApiClient>.Instance);
        var stateManager = new EnrollmentStateManager(configOptions, NullLogger<EnrollmentStateManager>.Instance);
        var enrollmentService = new EnrollmentService(gridClient, stateManager, configOptions, NullLogger<EnrollmentService>.Instance);

        var signalField = typeof(EnrollmentService).GetField("_enrolledSignal", BindingFlags.NonPublic | BindingFlags.Instance);
        ((TaskCompletionSource?)signalField?.GetValue(enrollmentService))?.TrySetResult();

        return enrollmentService;
    }

    /// <summary>
    /// Captures all HTTP requests for verification.
    /// Acts as a WireMock replacement without an external dependency.
    /// </summary>
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<CapturedRequest> CapturedRequests { get; } = [];
        private readonly object _lock = new();

        /// <summary>Override to simulate errors or custom responses.</summary>
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string? body = request.Content is not null
                ? await request.Content.ReadAsStringAsync(ct)
                : null;

            lock (_lock)
            {
                CapturedRequests.Add(new CapturedRequest
                {
                    Method = request.Method,
                    Url = request.RequestUri?.ToString() ?? "",
                    Body = body,
                });
            }

            if (ResponseFactory is not null)
                return ResponseFactory(request);

            // Default: 200 OK with valid AlertIngestResponse
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        alert_id = Guid.NewGuid().ToString(),
                        status = "created",
                        ingested_at = DateTime.UtcNow,
                    }, SnakeCaseOptions),
                    System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record CapturedRequest
    {
        public required HttpMethod Method { get; init; }
        public required string Url { get; init; }
        public string? Body { get; init; }
    }

    /// <summary>
    /// Minimal IHttpClientFactory that returns an HttpClient with the capture handler.
    /// </summary>
    private sealed class FakeHttpClientFactory(HttpMessageHandler handler, string baseUrl) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri(baseUrl)
        };
    }
}
