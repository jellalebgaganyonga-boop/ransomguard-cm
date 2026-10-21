using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Core.Detection.Sentinel.Response;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Service;
using RansomGuard.Agent.Tests.Detection.Sentinel.Response;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Communication;

/// <summary>
/// The agent executes the console's commands (J1 § 1.3) through the response engine, and reports
/// the truth: executed, or not, with a closed reason. Nothing else is ever claimed.
/// </summary>
public sealed class GridCommandServiceTests : IDisposable
{
    private static readonly DateTime Created = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);
    private const int Writer = 4242;

    private readonly MonitorHarness _h = new();
    private readonly FakeSuspender _suspender = new();
    private readonly Mock<IGridApiClient> _grid = new();
    private readonly List<AgentCommand> _queued = [];
    private readonly List<(string Id, CommandResultRequest Result)> _results = [];
    private readonly Dictionary<int, DateTime?> _running = new() { [Writer] = Created };

    public GridCommandServiceTests()
    {
        _grid.Setup(g => g.FetchCommandsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _queued.ToList());
        _grid.Setup(g => g.SendCommandResultAsync(It.IsAny<string>(), It.IsAny<CommandResultRequest>(), It.IsAny<CancellationToken>()))
            .Callback((string id, CommandResultRequest r, CancellationToken _) => _results.Add((id, r)))
            .Returns(Task.CompletedTask);
    }

    public void Dispose() => _h.Dispose();

    private CanaryResponseEngine Engine() => new(
        _suspender, _h.ScopeFactory, _h.AlertQueue,
        new SentinelResponseOptions { SuspendProcess = true, EscalationMinutes = 60 },
        NullLogger<CanaryResponseEngine>.Instance,
        readStartTime: pid => _running.TryGetValue(pid, out DateTime? at) ? at : null,
        agentPid: 1, resumeRetryDelay: TimeSpan.Zero);

    private GridCommandService Service(CanaryResponseEngine? engine) =>
        new(_grid.Object, _h.ScopeFactory, NullLogger<GridCommandService>.Instance, response: engine);

    private static AgentCommand Command(string id, string type, object parameters) => new()
    {
        Id = id,
        CommandType = type,
        Parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(parameters))!,
        IssuedAt = DateTime.UtcNow,
    };

    private CanaryAlert LocalAlert()
    {
        var alert = new CanaryAlert
        {
            CanaryId = Guid.NewGuid(), CanaryPath = @"C:\Users\alice\Documents\0001_x.txt",
            AlertType = CanaryAlertType.CanaryModified,
        };
        using IServiceScope scope = _h.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        db.CanaryAlerts.Add(alert);
        db.SaveChanges();
        return alert;
    }

    [Fact]
    public async Task Suspend_process_suspends_the_attributed_writer_of_a_known_alert_and_reports_the_suspension()
    {
        CanaryAlert alert = LocalAlert();
        _queued.Add(Command("c1", "suspend_process", new
        {
            alert_client_message_id = AlertMapper.DeterministicId(alert.Id), pid = Writer,
            process_created_at = Created.ToString("o"),
        }));

        (await Service(Engine()).ProcessOnceAsync(CancellationToken.None)).ShouldBe(1);

        _suspender.Suspended.ShouldBe([Writer]);
        (string id, CommandResultRequest result) = _results.ShouldHaveSingleItem();
        id.ShouldBe("c1");
        result.Success.ShouldBeTrue();
        result.Payload["state"].ShouldBe("suspended");
        result.Payload["suspension_id"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Suspend_process_for_an_alert_this_agent_does_not_know_is_refused_and_says_so()
    {
        _queued.Add(Command("c1", "suspend_process", new
        {
            alert_client_message_id = "rg-unknown", pid = Writer, process_created_at = Created.ToString("o"),
        }));

        await Service(Engine()).ProcessOnceAsync(CancellationToken.None);

        _suspender.Suspended.ShouldBeEmpty();
        _results.Single().Result.Success.ShouldBeFalse();
        _results.Single().Result.Payload["reason"].ShouldBe(ResponseReason.UnknownAlert);
    }

    [Fact]
    public async Task Resume_process_resumes_a_suspension_of_this_agent_and_an_unknown_one_is_refused()
    {
        CanaryResponseEngine engine = Engine();
        ResponseOutcome suspended = await engine.RespondAsync(
            new AttributionOutcome(AttributionState.Resolved, Writer, Created, "evil", null), @"C:\x\0001_y.txt", Guid.NewGuid(), "rg-origin");
        _queued.Add(Command("c1", "resume_process", new { suspension_id = suspended.SuspensionId!.Value.ToString() }));
        _queued.Add(Command("c2", "resume_process", new { suspension_id = Guid.NewGuid().ToString() }));

        await Service(engine).ProcessOnceAsync(CancellationToken.None);

        _suspender.Resumed.ShouldBe([Writer]);
        _results.Single(r => r.Id == "c1").Result.Success.ShouldBeTrue();
        _results.Single(r => r.Id == "c2").Result.Success.ShouldBeFalse();
        _results.Single(r => r.Id == "c2").Result.Payload["reason"].ShouldBe("resume_failed");
    }

    [Fact]
    public async Task Isolate_and_unknown_commands_are_answered_not_executed_never_silently_dropped()
    {
        _queued.Add(Command("iso", "isolate", new { reason = "x" }));
        _queued.Add(Command("odd", "reboot", new { }));

        await Service(Engine()).ProcessOnceAsync(CancellationToken.None);

        _results.Single(r => r.Id == "iso").Result.Payload["reason"].ShouldBe("not_implemented"); // AGT-ISO-001
        _results.Single(r => r.Id == "odd").Result.Payload["reason"].ShouldBe("unsupported_command");
        _results.ShouldAllBe(r => !r.Result.Success);
    }

    [Fact]
    public async Task Without_a_response_engine_the_process_commands_say_so()
    {
        _queued.Add(Command("c1", "resume_process", new { suspension_id = Guid.NewGuid().ToString() }));

        await Service(engine: null).ProcessOnceAsync(CancellationToken.None);

        _results.Single().Result.Payload["reason"].ShouldBe("no_response_engine");
    }

    [Fact]
    public async Task A_command_fetched_twice_is_executed_once_and_answered_once()
    {
        _queued.Add(Command("iso", "isolate", new { reason = "x" }));
        GridCommandService service = Service(Engine());

        (await service.ProcessOnceAsync(CancellationToken.None)).ShouldBe(1);
        (await service.ProcessOnceAsync(CancellationToken.None)).ShouldBe(0);

        _results.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_result_the_GRID_did_not_take_is_not_marked_done_so_the_command_is_answered_again()
    {
        _queued.Add(Command("iso", "isolate", new { reason = "x" }));
        _grid.Setup(g => g.SendCommandResultAsync("iso", It.IsAny<CommandResultRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));
        GridCommandService service = Service(Engine());

        (await service.ProcessOnceAsync(CancellationToken.None)).ShouldBe(0);
        _grid.Setup(g => g.SendCommandResultAsync("iso", It.IsAny<CommandResultRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        (await service.ProcessOnceAsync(CancellationToken.None)).ShouldBe(1);
    }
}
