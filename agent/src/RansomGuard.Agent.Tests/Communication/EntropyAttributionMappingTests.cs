using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Communication;

/// <summary>
/// An ENTROPY alert carries its writer (AGT-ATTR-001), using the same detail keys SENTINEL uses so
/// the GRID reads both the same way -- `_attribution_for()` on the server looks for
/// attribution_state / attribution_reason / process_* whatever the module. No new server contract.
///
/// The refusals matter as much as the resolution: when nobody can be named the alert says which
/// kind of "nobody", so the console can write "kernel monitoring unavailable" rather than implying
/// the file wrote itself.
/// </summary>
public sealed class EntropyAttributionMappingTests
{
    private static EntropyAlert Alert(string severity = "Critical") => new()
    {
        FilePath = @"C:\Users\vboxuser\Documents\patient.docx",
        RuleId = 1,
        RuleName = "AbsoluteHighEntropy",
        Severity = severity,
        BaselineEntropy = 4.2,
        CurrentEntropy = 7.92,
        Delta = 3.72,
    };

    private static string? Detail(AlertIngestRequest request, string key) =>
        request.Details.FirstOrDefault(d => d.Key == key)?.Value;

    [Fact]
    public void Without_an_attribution_the_alert_keeps_its_previous_shape()
    {
        // Every existing caller passes no attribution; none of them may change behaviour.
        AlertIngestRequest? request = AlertMapper.FromEntropyAlert(Alert());

        request.ShouldNotBeNull();
        request.Details.Count.ShouldBe(4);
        Detail(request, "attribution_state").ShouldBeNull();
        request.Summary.ShouldNotContain("written by");
    }

    [Fact]
    public void A_resolved_writer_is_carried_in_the_details_and_named_in_the_summary()
    {
        var created = new DateTime(2026, 10, 25, 11, 0, 0, DateTimeKind.Utc);
        var attribution = new AttributionOutcome(
            AttributionState.Resolved, 4521, created, "cryptolocker", @"C:\ProgramData\cryptolocker.exe");

        AlertIngestRequest? request = AlertMapper.FromEntropyAlert(Alert(), attribution);

        request.ShouldNotBeNull();
        Detail(request, "attribution_state").ShouldBe("resolved");
        Detail(request, "process_id").ShouldBe("4521");
        Detail(request, "process_name").ShouldBe("cryptolocker");
        Detail(request, "process_path").ShouldBe(@"C:\ProgramData\cryptolocker.exe");
        Detail(request, "process_created_at").ShouldBe(created.ToString("O"));
        // The list view shows the summary alone; "who" belongs there.
        request.Summary.ShouldContain("written by cryptolocker (PID 4521)");
    }

    [Fact]
    public void An_ambiguous_attribution_names_nobody()
    {
        AlertIngestRequest? request = AlertMapper.FromEntropyAlert(
            Alert(), new AttributionOutcome(AttributionState.Ambiguous));

        request.ShouldNotBeNull();
        Detail(request, "attribution_state").ShouldBe("ambiguous");
        Detail(request, "process_id").ShouldBeNull();
        Detail(request, "process_name").ShouldBeNull();
        request.Summary.ShouldNotContain("written by");
    }

    [Theory]
    [InlineData(AttributionReason.EtwUnavailable)]
    [InlineData(AttributionReason.NoWriteEvent)]
    [InlineData(AttributionReason.PidReused)]
    [InlineData(AttributionReason.ProcessExited)]
    [InlineData(AttributionReason.SystemPid)]
    public void An_unavailable_attribution_carries_the_reason_and_names_nobody(string reason)
    {
        AlertIngestRequest? request = AlertMapper.FromEntropyAlert(
            Alert(), new AttributionOutcome(AttributionState.Unavailable, Reason: reason));

        request.ShouldNotBeNull();
        Detail(request, "attribution_state").ShouldBe("unavailable");
        Detail(request, "attribution_reason").ShouldBe(reason);
        Detail(request, "process_id").ShouldBeNull();
        request.Summary.ShouldNotContain("written by");
    }

    [Fact]
    public void A_suppressed_alert_is_still_not_forwarded_even_with_a_writer()
    {
        AlertMapper.FromEntropyAlert(
                Alert(severity: "Suppressed"),
                new AttributionOutcome(AttributionState.Resolved, 4521, DateTime.UtcNow, "x"))
            .ShouldBeNull();
    }

    [Fact]
    public void A_resolved_writer_with_no_name_still_carries_its_pid()
    {
        // A protected process: the PID pair stands, the image and name are unreadable.
        AlertIngestRequest? request = AlertMapper.FromEntropyAlert(
            Alert(), new AttributionOutcome(AttributionState.Resolved, 4521, DateTime.UtcNow));

        request.ShouldNotBeNull();
        Detail(request, "process_id").ShouldBe("4521");
        Detail(request, "process_name").ShouldBeNull();
        Detail(request, "process_path").ShouldBeNull();
    }

    [Fact]
    public void The_summary_stays_within_the_GRID_limit_with_a_long_process_path()
    {
        var attribution = new AttributionOutcome(
            AttributionState.Resolved, 4521, DateTime.UtcNow,
            new string('n', 300), @"C:\" + new string('p', 300) + ".exe");

        AlertIngestRequest? request = AlertMapper.FromEntropyAlert(Alert(), attribution);

        request.ShouldNotBeNull();
        request.Summary.Length.ShouldBeLessThanOrEqualTo(500);
    }
}
