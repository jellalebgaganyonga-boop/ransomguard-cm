using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.IndicatorRemoval;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.IndicatorRemoval;

/// <summary>
/// Tests for all B.4 indicator removal detectors and multi-stage kill chain correlation.
/// 10+ tests covering: EventLogClearing, UsnJournalClearing, DefenderTampering,
/// SchedTaskTampering, MultiStageKillChain, and IT whitelist suppression.
/// </summary>
public sealed class IndicatorRemovalTests
{
    // ===== EventLogClearingDetector =====

    [Fact]
    public void EventLogClearing_WevtutilCl_Fires()
    {
        var detector = new EventLogClearingDetector();
        var process = CreateProcess("wevtutil", "wevtutil cl Security");

        var result = detector.Evaluate(process);

        result.ShouldNotBeNull();
        result.EventType.ShouldBe(IndicatorRemovalType.EventLogClearing);
        result.MitreTechniqueId.ShouldBe("T1070.001");
        result.Severity.ShouldBe("Critical");
        result.TargetResource.ShouldBe("security");
        result.WhitelistSuppressed.ShouldBeFalse();
    }

    [Fact]
    public void EventLogClearing_ClearEventLog_Fires()
    {
        var detector = new EventLogClearingDetector();
        var process = CreateProcess("powershell", "powershell -Command Clear-EventLog -LogName System");

        var result = detector.Evaluate(process);

        result.ShouldNotBeNull();
        result.EventType.ShouldBe(IndicatorRemovalType.EventLogClearing);
        result.TargetResource.ShouldBe("system");
    }

    [Fact]
    public void EventLogClearing_WevtutilQuery_DoesNotFire()
    {
        var detector = new EventLogClearingDetector();
        var process = CreateProcess("wevtutil", "wevtutil qe Security /c:10");

        detector.Evaluate(process).ShouldBeNull();
    }

    // ===== UsnJournalClearingDetector =====

    [Fact]
    public void UsnJournalClearing_FsutilDeleteJournal_Fires()
    {
        var detector = new UsnJournalClearingDetector();
        var process = CreateProcess("fsutil", "fsutil usn deletejournal /d C:");

        var result = detector.Evaluate(process);

        result.ShouldNotBeNull();
        result.EventType.ShouldBe(IndicatorRemovalType.UsnJournalClearing);
        result.MitreTechniqueId.ShouldBe("T1070.004");
        result.Severity.ShouldBe("Critical");
        result.TargetResource.ShouldBe("C:");
    }

    [Fact]
    public void UsnJournalClearing_FsutilQueryJournal_DoesNotFire()
    {
        var detector = new UsnJournalClearingDetector();
        var process = CreateProcess("fsutil", "fsutil usn queryjournal C:");

        detector.Evaluate(process).ShouldBeNull();
    }

    // ===== DefenderTamperingDetector =====

    [Fact]
    public void DefenderTampering_DisableRealtime_Fires()
    {
        var detector = new DefenderTamperingDetector();
        var process = CreateProcess("powershell",
            "powershell Set-MpPreference -DisableRealtimeMonitoring $true");

        var result = detector.Evaluate(process);

        result.ShouldNotBeNull();
        result.EventType.ShouldBe(IndicatorRemovalType.DefenderTampering);
        result.MitreTechniqueId.ShouldBe("T1562.001");
        result.Severity.ShouldBe("Critical");
    }

    [Fact]
    public void DefenderTampering_EnableRealtime_DoesNotFire()
    {
        var detector = new DefenderTamperingDetector();
        var process = CreateProcess("powershell",
            "powershell Set-MpPreference -DisableRealtimeMonitoring $false");

        detector.Evaluate(process).ShouldBeNull();
    }

    // ===== SchedTaskTamperingDetector =====

    [Fact]
    public void SchedTaskTampering_DeleteDefenderTask_Fires()
    {
        var detector = new SchedTaskTamperingDetector();
        var process = CreateProcess("schtasks",
            "schtasks /delete /tn \"Windows Defender Scheduled Scan\" /f");

        var result = detector.Evaluate(process);

        result.ShouldNotBeNull();
        result.EventType.ShouldBe(IndicatorRemovalType.ScheduledTaskTampering);
        result.MitreTechniqueId.ShouldBe("T1562.001");
        result.Severity.ShouldBe("High");
    }

    [Fact]
    public void SchedTaskTampering_DeleteNonSecurityTask_DoesNotFire()
    {
        var detector = new SchedTaskTamperingDetector();
        var process = CreateProcess("schtasks",
            "schtasks /delete /tn \"MyAppBackup\" /f");

        detector.Evaluate(process).ShouldBeNull();
    }

    // ===== IT Whitelist Suppression =====

    [Fact]
    public void Whitelist_SystemProcess_Suppressed()
    {
        var detector = new EventLogClearingDetector();
        // wevtutil running from System32 is whitelisted
        var process = new ProcessSnapshot
        {
            ProcessId = 1000, ParentProcessId = 4,
            ProcessName = "wevtutil",
            ExecutablePath = @"C:\Windows\System32\wevtutil.exe",
            CommandLine = "wevtutil cl Setup"
        };

        var result = detector.Evaluate(process);

        result.ShouldNotBeNull();
        result.WhitelistSuppressed.ShouldBeTrue();
        result.Severity.ShouldBe("Low");
        result.ActionTaken.ShouldBe("Suppressed");
    }

    // ===== MultiStageKillChainDetector =====

    [Fact]
    public void KillChain_TwoStages_Within5Min_Fires()
    {
        var killChain = new MultiStageKillChainDetector();

        var evt1 = new IndicatorRemovalEvent
        {
            EventType = IndicatorRemovalType.EventLogClearing,
            MitreTechniqueId = "T1070.001", ProcessId = 1000,
            ProcessName = "wevtutil", CommandLine = "wevtutil cl Security",
            TargetResource = "Security", Severity = "Critical",
            Description = "test", ActionTaken = "Alert"
        };

        var evt2 = new IndicatorRemovalEvent
        {
            EventType = IndicatorRemovalType.UsnJournalClearing,
            MitreTechniqueId = "T1070.004", ProcessId = 1001,
            ProcessName = "fsutil", CommandLine = "fsutil usn deletejournal /d C:",
            TargetResource = "C:", Severity = "Critical",
            Description = "test", ActionTaken = "Alert"
        };

        // First event — no correlation yet
        killChain.RecordAndCorrelate(evt1).ShouldBeNull();

        // Second event — triggers kill chain
        var correlation = killChain.RecordAndCorrelate(evt2);

        correlation.ShouldNotBeNull();
        correlation.EventType.ShouldBe(IndicatorRemovalType.KillChainCorrelation);
        correlation.Severity.ShouldBe("Critical");
        correlation.Description.ShouldContain("RANSOMWARE KILL CHAIN");
        correlation.KillChainCorrelationId.ShouldNotBeNull();
        killChain.CorrelationCount.ShouldBe(1);
    }

    [Fact]
    public void KillChain_SingleStage_DoesNotFire()
    {
        var killChain = new MultiStageKillChainDetector();

        var evt = new IndicatorRemovalEvent
        {
            EventType = IndicatorRemovalType.EventLogClearing,
            MitreTechniqueId = "T1070.001", ProcessId = 1000,
            ProcessName = "wevtutil", CommandLine = "wevtutil cl Security",
            TargetResource = "Security", Severity = "Critical",
            Description = "test", ActionTaken = "Alert"
        };

        killChain.RecordAndCorrelate(evt).ShouldBeNull();
    }

    [Fact]
    public void KillChain_WhitelistedEventsIgnored()
    {
        var killChain = new MultiStageKillChainDetector();

        var suppressed = new IndicatorRemovalEvent
        {
            EventType = IndicatorRemovalType.EventLogClearing,
            MitreTechniqueId = "T1070.001", ProcessId = 1000,
            ProcessName = "trustedinstaller.exe", CommandLine = "wevtutil cl Setup",
            TargetResource = "Setup", Severity = "Low",
            Description = "suppressed", ActionTaken = "Suppressed",
            WhitelistSuppressed = true
        };

        killChain.RecordAndCorrelate(suppressed).ShouldBeNull();
    }

    // ===== Helpers =====

    private static ProcessSnapshot CreateProcess(string name, string commandLine) => new()
    {
        ProcessId = 1000,
        ParentProcessId = 500,
        ProcessName = name,
        ExecutablePath = $@"C:\Users\attacker\{name}",
        CommandLine = commandLine
    };
}
