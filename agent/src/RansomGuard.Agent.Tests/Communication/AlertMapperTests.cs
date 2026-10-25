using System.Reflection;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Communication;

public sealed class AlertMapperTests
{
    // ===== Test 1: CanaryAlert is mapped correctly =====
    [Fact]
    public void CanaryAlert_Mapped_Correctly()
    {
        var alert = new CanaryAlert
        {
            CanaryId = Guid.NewGuid(),
            CanaryPath = @"C:\Users\Desktop\0001_dossier_patient.docx",
            AlertType = CanaryAlertType.CanaryModified,
            Severity = AlertSeverity.Critical,
            OffendingProcessId = 1234,
            OffendingProcessName = "ransomware.exe",
            OffendingProcessPath = @"C:\temp\ransomware.exe",
        };

        var request = AlertMapper.FromCanaryAlert(alert);

        request.AlertType.ShouldBe("SentinelCanaryModified");
        request.Severity.ShouldBe("Critical");
        request.MitreTechniqueId.ShouldBe("T1486");
        request.Summary.ShouldContain("Canary file");
        request.Summary.ShouldContain("0001_dossier_patient.docx");
        request.ClientMessageId.Length.ShouldBe(64); // SHA256 hex
        request.Details.Count.ShouldBe(3); // process_id, process_name, process_path
    }

    // ===== Test 2: ClientMessageId is deterministic =====
    [Fact]
    public void ClientMessageId_Is_Deterministic_Same_Alert_Same_Id()
    {
        var id = Guid.NewGuid();
        var alert1 = new CanaryAlert { CanaryId = id, CanaryPath = "test", AlertType = CanaryAlertType.CanaryModified };
        var alert2 = new CanaryAlert { CanaryId = id, CanaryPath = "test", AlertType = CanaryAlertType.CanaryModified };
        // Force same Id
        var r1 = AlertMapper.DeterministicId(alert1.Id);
        var r2 = AlertMapper.DeterministicId(alert1.Id);

        r1.ShouldBe(r2);
    }

    // ===== Test 3: ClientMessageId is different for different alerts =====
    [Fact]
    public void ClientMessageId_Is_Different_For_Different_Alerts()
    {
        var id1 = AlertMapper.DeterministicId(Guid.NewGuid());
        var id2 = AlertMapper.DeterministicId(Guid.NewGuid());

        id1.ShouldNotBe(id2);
    }

    // ===== Test 4: EntropyAlert mapped correctly =====
    [Fact]
    public void EntropyAlert_Mapped_Correctly()
    {
        var alert = new EntropyAlert
        {
            FilePath = @"C:\Users\Documents\report.docx",
            RuleId = 1,
            RuleName = "AbsoluteHigh",
            Severity = "Critical",
            BaselineEntropy = 4.2,
            CurrentEntropy = 7.9,
            Delta = 3.7,
        };

        var request = AlertMapper.FromEntropyAlert(alert);

        request.ShouldNotBeNull();
        request.AlertType.ShouldBe("EntropyAbsoluteHigh");
        request.Severity.ShouldBe("Critical");
        request.Summary.ShouldContain("7.90");
        request.Details.Count.ShouldBe(4);
    }

    // ===== Test 5: Suppressed EntropyAlert returns null =====
    [Fact]
    public void Suppressed_EntropyAlert_Returns_Null()
    {
        var alert = new EntropyAlert
        {
            FilePath = @"C:\file.zip",
            RuleId = 4,
            RuleName = "ExtensionWhitelist",
            Severity = "Suppressed",
            BaselineEntropy = 0,
            CurrentEntropy = 7.9,
            Delta = 0,
        };

        AlertMapper.FromEntropyAlert(alert).ShouldBeNull();
    }

    // ===== Test 6: UsbAlert mapped correctly =====
    [Fact]
    public void UsbAlert_Mapped_Correctly()
    {
        var alert = new UsbAlert
        {
            UsbScanResultId = Guid.NewGuid(),
            Title = "USB GUARD: Critical — Suspicious USB Drive",
            Description = "Bootable USB detected with autorun.inf",
            Severity = "Critical",
            ActionTaken = "BlockAndEject",
        };

        var request = AlertMapper.FromUsbAlert(alert);

        request.AlertType.ShouldBe("UsbGuard");
        request.MitreTechniqueId.ShouldBe("T1091");
        request.Severity.ShouldBe("Critical");
    }

    // ===== Test 7: ExfilFinding mapped correctly =====
    [Fact]
    public void ExfilFinding_Mapped_Correctly()
    {
        var finding = new ExfilFinding
        {
            RuleName = "VolumeAnomaly",
            Severity = ExfilSeverity.High,
            Description = "Volume anomaly detected",
            ProcessId = 5678,
            ProcessName = "powershell.exe",
            Destination = "45.33.32.1",
            BytesTransferred = 50_000_000,
            MitreId = "T1041",
        };

        var request = AlertMapper.FromExfilFinding(finding);

        request.AlertType.ShouldBe("ExfilWatchVolumeAnomaly");
        request.Severity.ShouldBe("High");
        request.MitreTechniqueId.ShouldBe("T1041");
        request.ClientMessageId.Length.ShouldBe(64);
    }

    // ===== Test 8: IndicatorRemovalEvent mapped correctly =====
    [Fact]
    public void IndicatorRemovalEvent_Mapped_Correctly()
    {
        var evt = new IndicatorRemovalEvent
        {
            EventType = IndicatorRemovalType.EventLogClearing,
            MitreTechniqueId = "T1070.001",
            ProcessId = 999,
            ProcessName = "wevtutil.exe",
            CommandLine = "wevtutil cl Security",
            TargetResource = "Security",
            Severity = "Critical",
            Description = "Security event log cleared",
            ActionTaken = "Alert",
        };

        var request = AlertMapper.FromIndicatorRemovalEvent(evt);

        request.AlertType.ShouldBe("IndicatorRemovalEventLogClearing");
        request.MitreTechniqueId.ShouldBe("T1070.001");
        request.Severity.ShouldBe("Critical");
    }

    // ===== Test 9: Exfil same rule same minute produces same client_message_id (dedup) =====
    [Fact]
    public void Exfil_Same_Rule_Same_Minute_Deduped_As_Duplicate()
    {
        var baseTime = new DateTime(2026, 7, 1, 14, 30, 0, DateTimeKind.Utc);

        var f1 = new ExfilFinding
        {
            RuleName = "VolumeAnomaly",
            Severity = ExfilSeverity.High,
            Description = "desc1",
            ProcessId = 100,
            ProcessName = "proc",
            Destination = "10.0.0.1",
            BytesTransferred = 1000,
            MitreId = "T1041",
            DetectedAt = baseTime.AddSeconds(10), // same minute
        };

        var f2 = new ExfilFinding
        {
            RuleName = "VolumeAnomaly",
            Severity = ExfilSeverity.High,
            Description = "desc2",
            ProcessId = 100,
            ProcessName = "proc",
            Destination = "10.0.0.1",
            BytesTransferred = 2000,
            MitreId = "T1041",
            DetectedAt = baseTime.AddSeconds(45), // same minute
        };

        var id1 = AlertMapper.DeterministicExfilId(f1);
        var id2 = AlertMapper.DeterministicExfilId(f2);

        id1.ShouldBe(id2, "Same rule+process+dest in same minute should produce same ID for dedup");
    }

    // ===== Test 10: Exfil different minute produces different client_message_id =====
    [Fact]
    public void Exfil_Different_Minute_Produces_Different_Id()
    {
        var f1 = new ExfilFinding
        {
            RuleName = "VolumeAnomaly",
            Severity = ExfilSeverity.High,
            Description = "desc",
            ProcessId = 100,
            ProcessName = "proc",
            Destination = "10.0.0.1",
            BytesTransferred = 1000,
            MitreId = "T1041",
            DetectedAt = new DateTime(2026, 7, 1, 14, 30, 0, DateTimeKind.Utc),
        };

        var f2 = new ExfilFinding
        {
            RuleName = "VolumeAnomaly",
            Severity = ExfilSeverity.High,
            Description = "desc",
            ProcessId = 100,
            ProcessName = "proc",
            Destination = "10.0.0.1",
            BytesTransferred = 1000,
            MitreId = "T1041",
            DetectedAt = new DateTime(2026, 7, 1, 14, 31, 0, DateTimeKind.Utc), // next minute
        };

        AlertMapper.DeterministicExfilId(f1).ShouldNotBe(AlertMapper.DeterministicExfilId(f2));
    }

    // ===== Test 11: ClientMessageId length within GRID constraint 10-64 =====
    [Fact]
    public void ClientMessageId_Length_Within_Grid_Constraint()
    {
        var id = AlertMapper.DeterministicId(Guid.NewGuid());
        id.Length.ShouldBeGreaterThanOrEqualTo(10);
        id.Length.ShouldBeLessThanOrEqualTo(64);
    }

    // ===== Test 12: Summary truncated at 500 chars =====
    [Fact]
    public void Summary_Truncated_At_500_Chars()
    {
        var longPath = new string('x', 600);
        var alert = new CanaryAlert
        {
            CanaryId = Guid.NewGuid(),
            CanaryPath = longPath,
            AlertType = CanaryAlertType.CanaryModified,
        };

        var request = AlertMapper.FromCanaryAlert(alert);
        request.Summary.Length.ShouldBeLessThanOrEqualTo(500);
    }

    // ===== Test 13: AlertType max 50 chars =====
    [Fact]
    public void AlertType_Within_50_Chars()
    {
        var alert = new CanaryAlert
        {
            CanaryId = Guid.NewGuid(),
            CanaryPath = "test",
            AlertType = CanaryAlertType.CanaryModified,
        };
        AlertMapper.FromCanaryAlert(alert).AlertType.Length.ShouldBeLessThanOrEqualTo(50);

        var finding = new ExfilFinding
        {
            RuleName = "VolumeAnomaly",
            Severity = ExfilSeverity.High,
            Description = "d",
            ProcessId = 1,
            ProcessName = "p",
            Destination = "1.2.3.4",
            BytesTransferred = 1,
            MitreId = "T1041",
        };
        AlertMapper.FromExfilFinding(finding).AlertType.Length.ShouldBeLessThanOrEqualTo(50);
    }

    // ===== Test 14: ExfilFinding.DetectedAt defaults to UtcNow =====
    [Fact]
    public void ExfilFinding_DetectedAt_Defaults_To_UtcNow()
    {
        var before = DateTime.UtcNow;
        var finding = new ExfilFinding
        {
            RuleName = "Test",
            Severity = ExfilSeverity.Low,
            Description = "d",
            ProcessId = 1,
            ProcessName = "p",
            Destination = "1.2.3.4",
            BytesTransferred = 1,
            MitreId = "T1041",
        };
        var after = DateTime.UtcNow;

        finding.DetectedAt.ShouldBeGreaterThanOrEqualTo(before);
        finding.DetectedAt.ShouldBeLessThanOrEqualTo(after);
    }

    // ===== Test 15: All severity values map to valid GRID enum =====
    [Theory]
    [InlineData("Low")]
    [InlineData("Medium")]
    [InlineData("High")]
    [InlineData("Critical")]
    public void All_Severity_Values_Are_Valid_Grid_Enum(string severity)
    {
        var alert = new EntropyAlert
        {
            FilePath = "test",
            RuleId = 1,
            RuleName = "Test",
            Severity = severity,
            BaselineEntropy = 0,
            CurrentEntropy = 7,
            Delta = 7,
        };

        var request = AlertMapper.FromEntropyAlert(alert);
        request.ShouldNotBeNull();

        string[] validValues = ["Low", "Medium", "High", "Critical"];
        validValues.ShouldContain(request.Severity);
    }

    // ===== Test 16: CanaryAlert without process attribution has empty details =====
    [Fact]
    public void CanaryAlert_No_Process_Attribution_Has_Empty_Details()
    {
        var alert = new CanaryAlert
        {
            CanaryId = Guid.NewGuid(),
            CanaryPath = "test",
            AlertType = CanaryAlertType.CanaryDeleted,
        };

        AlertMapper.FromCanaryAlert(alert).Details.ShouldBeEmpty();
    }

    // ===== Test 17: every client_message_id fits the GRID contract =====
    //
    // The GRID validates client_message_id as 10..64 characters (AlertIngestRequest schema) and
    // stores it in String(64). The hex of SHA-256 is exactly 64, so the closure and escalation
    // alerts, which appended a suffix to it, produced 66..77 characters and the GRID answered 422
    // on every one of them: a process resumed on the console stayed listed as suspended, because
    // the alert that says so never got in. These bounds are the GRID's, asserted here on the agent
    // side so the next discriminator cannot overflow them unnoticed.
    private const int GridClientMessageIdMin = 10;
    private const int GridClientMessageIdMax = 64;

    public static TheoryData<string, string> EverySuspensionMessageId()
    {
        var suspension = new SuspendedProcess
        {
            ProcessId = 3792,
            ProcessCreatedAt = DateTime.UtcNow,
            ProcessName = "cmd.exe",
            CanaryPath = @"C:\Users\Public\Documents\0001_dossier_patient.docx",
            AlertId = Guid.NewGuid(),
            ClientMessageId = AlertMapper.DeterministicId(Guid.NewGuid()),
            SuspendedAt = DateTime.UtcNow,
        };

        DateTime at = DateTime.UtcNow;
        var data = new TheoryData<string, string>
        {
            { "suspended", AlertMapper.FromProcessSuspension(suspension).ClientMessageId },
            { "escalation 1", AlertMapper.FromSuspensionEscalation(suspension, TimeSpan.FromMinutes(15), 1, at).ClientMessageId },
            { "escalation 12", AlertMapper.FromSuspensionEscalation(suspension, TimeSpan.FromHours(3), 12, at).ClientMessageId },
            { "isolation", AlertMapper.FromIsolationRecommended(Guid.NewGuid(), [@"C:\a.docx"], at).ClientMessageId },
        };

        // Every closure reason, read from the class so a new one is covered the day it is added.
        foreach (string close in typeof(AlertMapper.SuspensionClose)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!))
        {
            data.Add(close, AlertMapper.FromSuspensionClosed(suspension, close, "operator", at).ClientMessageId);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EverySuspensionMessageId))]
    public void Every_client_message_id_fits_what_the_Grid_accepts(string which, string clientMessageId)
    {
        clientMessageId.Length.ShouldBeInRange(
            GridClientMessageIdMin,
            GridClientMessageIdMax,
            $"client_message_id for '{which}' is {clientMessageId.Length} characters; the GRID rejects it with 422");
    }

    [Fact]
    public void The_alerts_about_one_suspension_each_get_their_own_id()
    {
        string[] ids = EverySuspensionMessageId().Select(row => (string)row[1]!).ToArray();

        // Distinct, or the GRID would take the second alert for a duplicate of the first and the
        // closure would be dropped as silently as it was rejected.
        ids.ShouldBeUnique();
    }

    [Fact]
    public void The_id_of_a_closure_is_the_same_every_time_it_is_built()
    {
        var suspension = new SuspendedProcess
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ProcessId = 3792,
            ProcessCreatedAt = DateTime.UnixEpoch,
            CanaryPath = @"C:\a.docx",
            AlertId = Guid.NewGuid(),
            ClientMessageId = "x".PadRight(20, 'x'),
            SuspendedAt = DateTime.UnixEpoch,
        };

        // Idempotency: a resend after a restart must be recognised as the same message.
        AlertMapper.FromSuspensionClosed(suspension, AlertMapper.SuspensionClose.Resumed, "a", DateTime.UnixEpoch)
            .ClientMessageId
            .ShouldBe(AlertMapper.FromSuspensionClosed(suspension, AlertMapper.SuspensionClose.Resumed, "b", DateTime.UtcNow)
                .ClientMessageId);
    }
}
