using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Security.Cryptography;
using RansomGuard.Agent.Service;
using Shouldly;

namespace RansomGuard.Agent.Tests.Communication;

/// <summary>
/// GRID-SEC-003: at enrollment the agent sends the public key of its OWN audit.key; no private key
/// travels, in either direction.
/// </summary>
public sealed class EnrollmentKeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rg-enroll-key-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void The_enrollment_response_carries_no_key_material()
    {
        PropertyInfo[] properties = typeof(EnrollmentResponse).GetProperties();

        properties.Select(p => p.Name).ShouldBe(["AgentId", "TenantId", "Message"], ignoreOrder: true);
    }

    [Fact]
    public async Task Enrollment_sends_the_public_key_of_the_agents_own_audit_key()
    {
        Directory.CreateDirectory(_dir);
        var signer = new AuditLogSigner(Path.Combine(_dir, "keys"), NullLogger<AuditLogSigner>.Instance);
        var handler = new CapturingHandler();
        EnrollmentService service = BuildService(signer, handler);

        await service.StartAsync(CancellationToken.None);
        await service.EnrolledTask.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        using JsonDocument body = JsonDocument.Parse(handler.Body.ShouldNotBeNull());
        string sent = body.RootElement.GetProperty("ed25519_public_key_b64").GetString().ShouldNotBeNull();
        sent.ShouldBe(signer.GetPublicKeyBase64());
        Convert.FromBase64String(sent).Length.ShouldBe(32);
        handler.Body.ShouldNotContain("PRIVATE", Case.Insensitive);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory does not fail the test.
        }
    }

    private EnrollmentService BuildService(AuditLogSigner signer, CapturingHandler handler)
    {
        var config = new AgentConfiguration
        {
            Identity = new AgentIdentityOptions { Id = "test-agent", Hostname = "TEST", Version = "0.4.0" },
            Server = new ServerOptions { BaseUrl = "https://grid.test", EnrollmentOtp = "RG-CAM-2026-X7K9P3" },
            Detection = new DetectionOptions { WatchPaths = [] },
            Database = new DatabaseOptions { ConnectionString = $"Data Source={Path.Combine(_dir, "agent.db")}" },
            Logging = new LoggingOptions { MinimumLevel = "Warning", LogFilePath = Path.Combine(_dir, "agent-.log") },
        };
        IOptions<AgentConfiguration> options = Options.Create(config);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));
        var gridClient = new GridApiClient(factory.Object, options, NullLogger<GridApiClient>.Instance);
        var stateManager = new EnrollmentStateManager(options, NullLogger<EnrollmentStateManager>.Instance);
        return new EnrollmentService(gridClient, stateManager, signer, options, NullLogger<EnrollmentService>.Instance);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"agent_id":"agent-1","tenant_id":"tenant-1","message":"Agent enrolled successfully"}""",
                    Encoding.UTF8, "application/json"),
            };
        }
    }
}
