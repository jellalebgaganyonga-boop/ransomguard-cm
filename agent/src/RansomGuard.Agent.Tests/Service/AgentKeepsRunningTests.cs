using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RansomGuard.Agent.Service;
using Shouldly;

namespace RansomGuard.Agent.Tests.Service;

/// <summary>
/// One service failing does not take the protection off the workstation.
///
/// <para>
/// .NET's default is <see cref="BackgroundServiceExceptionBehavior.StopHost"/>: an unhandled
/// exception anywhere in any <see cref="BackgroundService"/> stops the whole host. For a web
/// application that is the right default -- better a process that restarts than one serving
/// wrong answers. For an agent whose job is to be watching, it is the wrong one: the process
/// exits, with code 0 so nothing reads it as a crash, Windows does not restart it because it was
/// not a failure, and the workstation is unprotected until somebody looks.
/// </para>
///
/// <para>
/// That happened on PC-LAB on 2026-10-26. The GRID took more than the client's 10 s to answer a
/// poll for commands; the resulting <see cref="TaskCanceledException"/> escaped
/// <c>GridCommandService</c>; the host stopped. Fifty minutes later the console still said
/// disconnected and the service was still STOPPED with exit code 0.
/// </para>
///
/// <para>
/// The failing service is still a failure and is still reported -- the host logs it, and at
/// Warning the Event Log sink carries it off the machine. What it no longer does is take
/// SENTINEL, ENTROPY and USB GUARD down with it.
/// </para>
/// </summary>
public sealed class AgentKeepsRunningTests
{
    [Fact(Timeout = 30_000)]
    public async Task A_service_that_throws_does_not_stop_the_agent()
    {
        var threw = new TaskCompletionSource();

        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Services.AddLogging();
        ServiceRegistration.KeepTheAgentRunningWhenOneServiceFails(builder.Services);
        builder.Services.AddHostedService(_ => new ThrowingService(threw));

        using IHost host = builder.Build();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        await host.StartAsync();
        await threw.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The host reacts to the failure on its own schedule; give it room to do the wrong thing.
        await Task.Delay(TimeSpan.FromSeconds(1));

        lifetime.ApplicationStopping.IsCancellationRequested.ShouldBeFalse(
            "one service failing must not stop the agent: the workstation would be left unprotected");

        await host.StopAsync();
    }

    /// <summary>Fails the way a timed-out HTTP call does: after the host has started.</summary>
    private sealed class ThrowingService(TaskCompletionSource threw) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            threw.TrySetResult();
            throw new TaskCanceledException(
                "The request was canceled due to the configured HttpClient.Timeout of 10 seconds elapsing.",
                new TimeoutException("A task was canceled."));
        }
    }
}
