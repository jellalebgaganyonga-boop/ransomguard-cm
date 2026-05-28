using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Detection.IronClad;
using RansomGuard.Agent.Core.Detection.IronClad.Actions;
using RansomGuard.Agent.Core.Detection.IronClad.Communication;
using RansomGuard.Agent.Core.Detection.IronClad.Models;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Serilog;

namespace RansomGuard.Agent.Tests.Detection.IronClad;

public sealed class IronCladActionEngineTests : IDisposable
{
    private readonly AgentDbContext _db;
    private readonly IronCladEventRepository _eventRepo;
    private readonly IronCladDeviceStateRepository _stateRepo;
    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();

    public IronCladActionEngineTests()
    {
        var opts = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AgentDbContext(opts);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _eventRepo = new IronCladEventRepository(_db);
        _stateRepo = new IronCladDeviceStateRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    private IronCladActionEngine CreateEngine(
        bool enabled = true,
        bool connected = true,
        IronCladResponse? mockResponse = null)
    {
        var options = Options.Create(new IronCladOptions
        {
            Enabled = enabled,
            RelayCount = 4,
            CommandTimeoutSeconds = 5
        });

        var comm = new FakeCommunicator(connected, mockResponse);

        var auditLog = new FakeAuditLogRepository();

        return new IronCladActionEngine(comm, options, auditLog, _eventRepo, _stateRepo, _logger);
    }

    [Fact]
    public async Task CutUsbPort_When_Disabled_Returns_Disabled()
    {
        var engine = CreateEngine(enabled: false);

        var result = await engine.CutUsbPortAsync(1, "test", null);

        Assert.False(result.IsSuccess);
        Assert.Equal(IronCladActionOutcome.Disabled, result.Outcome);
    }

    [Fact]
    public async Task CutUsbPort_When_Device_Unavailable_Returns_DeviceUnavailable()
    {
        var engine = CreateEngine(connected: false);

        var result = await engine.CutUsbPortAsync(1, "test", null);

        Assert.False(result.IsSuccess);
        Assert.Equal(IronCladActionOutcome.DeviceUnavailable, result.Outcome);
    }

    [Fact]
    public async Task CutUsbPort_Invalid_Port_Returns_InvalidPort()
    {
        var engine = CreateEngine();

        var result = await engine.CutUsbPortAsync(99, "test", null);

        Assert.False(result.IsSuccess);
        Assert.Equal(IronCladActionOutcome.InvalidPort, result.Outcome);
    }

    [Fact]
    public async Task CutUsbPort_Success_Persists_Event_And_DeviceState()
    {
        var ackResponse = new IronCladResponse
        {
            CommandId = Guid.Empty,
            Type = IronCladResponseType.Ack,
            RawPayload = "ACK:PORT_3_CUT",
            ReceivedAt = DateTime.UtcNow,
            IsAcknowledgement = true
        };
        var engine = CreateEngine(mockResponse: ackResponse);

        var result = await engine.CutUsbPortAsync(3, "USB Critical threat", Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal(IronCladActionOutcome.Success, result.Outcome);

        // Verify event persisted
        var events = await _eventRepo.GetRecentAsync();
        Assert.Single(events);
        Assert.Equal("CutPort", events[0].Action);
        Assert.Equal("3", events[0].Parameter);

        // Verify device state updated
        var states = await _stateRepo.GetCurrentStatesAsync();
        Assert.Single(states);
        Assert.Equal(3, states[0].PortNumber);
        Assert.Equal("Cut", states[0].State);
    }

    [Fact]
    public async Task CutUsbPort_Timeout_Returns_Timeout_Outcome()
    {
        var timeoutResponse = new IronCladResponse
        {
            CommandId = Guid.Empty,
            Type = IronCladResponseType.Error,
            RawPayload = "",
            ReceivedAt = DateTime.UtcNow,
            IsAcknowledgement = false,
            ErrorCode = "TIMEOUT",
            ErrorMessage = "Command timed out"
        };
        var engine = CreateEngine(mockResponse: timeoutResponse);

        var result = await engine.CutUsbPortAsync(1, "test", null);

        Assert.False(result.IsSuccess);
        Assert.Equal(IronCladActionOutcome.Timeout, result.Outcome);
    }

    [Fact]
    public async Task CutUsbPort_AlreadyInState_Returns_AlreadyInState()
    {
        var e002Response = new IronCladResponse
        {
            CommandId = Guid.Empty,
            Type = IronCladResponseType.Error,
            RawPayload = "ERR:E002:Port already in requested state",
            ReceivedAt = DateTime.UtcNow,
            IsAcknowledgement = false,
            ErrorCode = "E002",
            ErrorMessage = "Port already in requested state"
        };
        var engine = CreateEngine(mockResponse: e002Response);

        var result = await engine.CutUsbPortAsync(1, "test", null);

        Assert.False(result.IsSuccess);
        Assert.Equal(IronCladActionOutcome.AlreadyInState, result.Outcome);
    }

    [Fact]
    public async Task RestoreUsbPort_Success_Updates_DeviceState_To_Active()
    {
        var ackResponse = new IronCladResponse
        {
            CommandId = Guid.Empty,
            Type = IronCladResponseType.Ack,
            RawPayload = "ACK:PORT_2_RESTORED",
            ReceivedAt = DateTime.UtcNow,
            IsAcknowledgement = true
        };
        var engine = CreateEngine(mockResponse: ackResponse);

        var result = await engine.RestoreUsbPortAsync(2, "Admin recovery");

        Assert.True(result.IsSuccess);

        var states = await _stateRepo.GetCurrentStatesAsync();
        Assert.Single(states);
        Assert.Equal("Active", states[0].State);
    }

    [Fact]
    public async Task CutAllPorts_Updates_All_Port_States()
    {
        var ackResponse = new IronCladResponse
        {
            CommandId = Guid.Empty,
            Type = IronCladResponseType.Ack,
            RawPayload = "ACK:ALL_PORTS_CUT",
            ReceivedAt = DateTime.UtcNow,
            IsAcknowledgement = true
        };
        var engine = CreateEngine(mockResponse: ackResponse);

        var result = await engine.CutAllPortsAsync("Emergency lockdown");

        Assert.True(result.IsSuccess);

        var states = await _stateRepo.GetCurrentStatesAsync();
        Assert.Equal(4, states.Count);
        Assert.All(states, s => Assert.Equal("Cut", s.State));
    }

    /// <summary>Fake communicator for unit tests.</summary>
    private sealed class FakeCommunicator : IIronCladCommunicator
    {
        private readonly bool _connected;
        private readonly IronCladResponse? _response;

        public FakeCommunicator(bool connected, IronCladResponse? response)
        {
            _connected = connected;
            _response = response;
        }

        public bool IsConnected => _connected;
        public string? DeviceVersion => "1.0.0-test";
        public int PortCount => 4;
        public event EventHandler<HeartbeatEventArgs>? HeartbeatReceived;
        public event EventHandler<ConnectionLostEventArgs>? ConnectionLost;

        public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(_connected);
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IronCladResponse> SendCommandAsync(IronCladCommand command, CancellationToken ct = default)
        {
            var resp = _response ?? new IronCladResponse
            {
                CommandId = command.Id,
                Type = IronCladResponseType.Ack,
                RawPayload = "ACK:OK",
                ReceivedAt = DateTime.UtcNow,
                IsAcknowledgement = true
            };
            // Suppress unused event warnings
            _ = HeartbeatReceived;
            _ = ConnectionLost;
            return Task.FromResult(resp with { CommandId = command.Id });
        }

        public Task<HealthStatus> CheckHealthAsync(CancellationToken ct = default)
            => Task.FromResult(new HealthStatus
            {
                IsConnected = _connected,
                DeviceVersion = "1.0.0-test",
                PortCount = 4,
                PortStates = new Dictionary<int, PortState>(),
                LastHeartbeatAt = DateTime.UtcNow,
                AverageLatency = TimeSpan.Zero,
                MissedHeartbeats = 0
            });

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Fake audit log for unit tests.</summary>
    private sealed class FakeAuditLogRepository : IAuditLogRepository
    {
        public Task AppendAsync(string action, string details, string? entityType = null, Guid? entityId = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<RansomGuard.Agent.Core.Persistence.Entities.AuditLog?> GetLatestAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<RansomGuard.Agent.Core.Persistence.Entities.AuditLog?>(null);

        public Task<IReadOnlyList<RansomGuard.Agent.Core.Persistence.Entities.AuditLog>> GetByTimeRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RansomGuard.Agent.Core.Persistence.Entities.AuditLog>>(Array.Empty<RansomGuard.Agent.Core.Persistence.Entities.AuditLog>());

        public Task<bool> VerifyChainIntegrityAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
