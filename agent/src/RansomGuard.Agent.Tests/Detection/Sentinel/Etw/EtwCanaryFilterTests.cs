using System.Diagnostics;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel.Etw;

/// <summary>
/// The filter between the ETW session and SENTINEL (ADR-025, commit 2): only canary writes pass,
/// in a bounded channel that never makes the session wait.
/// </summary>
public sealed class EtwCanaryFilterTests
{
    private const int AgentPid = 1000;
    private const int Writer = 4242;
    private const string Canary = @"C:\Patients\0001_dossier_patient.docx";
    private const string OtherCanary = @"C:\Comptabilite\0002_factures.xlsx";

    private readonly EtwCanaryFilter _filter = new(agentPid: AgentPid);

    public EtwCanaryFilterTests()
    {
        _filter.SetCanaries([Canary, OtherCanary]);
    }

    [Fact]
    public void A_write_on_a_canary_passes_whatever_the_case_of_the_path()
    {
        _filter.OnFileIo(Record(FileIoOperation.Write, @"c:\PATIENTS\0001_DOSSIER_PATIENT.DOCX"));

        Passed().ShouldHaveSingleItem().ProcessId.ShouldBe(Writer);
    }

    [Theory]
    [InlineData(@"C:\Patients\0001_dossier_patient.docx.bak")] // same directory, not a canary
    [InlineData(@"C:\Patients\notes.txt")]
    [InlineData(@"C:\Windows\System32\config\SOFTWARE")]        // another directory
    [InlineData(@"C:\Patients")]                                 // the directory itself
    public void A_write_on_anything_else_never_passes(string path)
    {
        _filter.OnFileIo(Record(FileIoOperation.Write, path));

        Passed().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(FileIoOperation.Write, 0u, 0)]
    [InlineData(FileIoOperation.Delete, 0u, 13)]
    [InlineData(FileIoOperation.Rename, 0u, 10)]
    [InlineData(FileIoOperation.SetInfo, 0u, 10)] // rename
    [InlineData(FileIoOperation.SetInfo, 0u, 65)] // rename, Ex
    [InlineData(FileIoOperation.SetInfo, 0u, 13)] // delete disposition
    [InlineData(FileIoOperation.SetInfo, 0u, 64)] // delete disposition, Ex
    [InlineData(FileIoOperation.SetInfo, 0u, 20)] // end of file
    [InlineData(FileIoOperation.Create, 0u, 0)]   // FILE_SUPERSEDE
    [InlineData(FileIoOperation.Create, 2u, 0)]   // FILE_CREATE: a canary recreated
    [InlineData(FileIoOperation.Create, 4u, 0)]   // FILE_OVERWRITE
    [InlineData(FileIoOperation.Create, 5u, 0)]   // FILE_OVERWRITE_IF: File.WriteAllBytes
    public void A_writing_operation_passes(FileIoOperation operation, uint disposition, int infoClass)
    {
        _filter.OnFileIo(Record(operation, Canary, disposition: disposition, infoClass: infoClass));

        Passed().ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(FileIoOperation.Create, 1u, 0)]  // FILE_OPEN: the antivirus, the indexer, a backup
    [InlineData(FileIoOperation.Create, 3u, 0)]  // FILE_OPEN_IF
    [InlineData(FileIoOperation.SetInfo, 0u, 4)] // FileBasicInformation: timestamps, e.g. restored by a scanner
    [InlineData(FileIoOperation.SetInfo, 0u, 19)] // FileAllocationInformation
    public void An_opening_or_a_metadata_change_never_passes(FileIoOperation operation, uint disposition, int infoClass)
    {
        _filter.OnFileIo(Record(operation, Canary, disposition: disposition, infoClass: infoClass));

        Passed().ShouldBeEmpty();
    }

    [Fact]
    public void A_write_carried_by_PID_4_passes_it_is_a_detection_without_attribution()
    {
        _filter.OnFileIo(Record(FileIoOperation.Write, Canary, pid: 4));

        Passed().ShouldHaveSingleItem().ProcessId.ShouldBe(4);
    }

    [Fact]
    public void The_agent_own_operations_never_pass()
    {
        _filter.OnFileIo(Record(FileIoOperation.Create, Canary, pid: AgentPid, disposition: 5)); // regeneration
        _filter.OnFileIo(Record(FileIoOperation.Write, Canary, pid: AgentPid));

        Passed().ShouldBeEmpty();
    }

    [Fact]
    public void The_canaries_are_replaced_in_one_block()
    {
        const string regenerated = @"C:\Patients\0003_ordonnance.pdf";

        _filter.SetCanaries([regenerated, OtherCanary]);
        _filter.OnFileIo(Record(FileIoOperation.Write, Canary));
        _filter.OnFileIo(Record(FileIoOperation.Write, regenerated));

        Passed().ShouldHaveSingleItem().FileName.ShouldBe(regenerated);
    }

    [Fact]
    public void Before_any_canary_is_known_nothing_passes()
    {
        var empty = new EtwCanaryFilter(agentPid: AgentPid);

        empty.OnFileIo(Record(FileIoOperation.Write, Canary));

        empty.Reader.TryRead(out _).ShouldBeFalse();
    }

    [Fact]
    public void A_full_channel_keeps_the_first_records_and_counts_the_rest_as_lost()
    {
        for (int i = 0; i < EtwCanaryFilter.Capacity + 25; i++)
        {
            _filter.OnFileIo(Record(FileIoOperation.Write, Canary, pid: 10_000 + i));
        }

        _filter.Lost.ShouldBe(25);
        List<FileIoRecord> passed = Passed();
        passed.Count.ShouldBe(EtwCanaryFilter.Capacity);
        passed[0].ProcessId.ShouldBe(10_000); // the first writer is kept: it is the one to attribute
    }

    [Fact]
    public void The_callback_never_waits_even_on_a_full_channel()
    {
        for (int i = 0; i < EtwCanaryFilter.Capacity; i++)
        {
            _filter.OnFileIo(Record(FileIoOperation.Write, Canary));
        }

        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 100_000; i++)
        {
            _filter.OnFileIo(Record(FileIoOperation.Write, Canary));
        }

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2), "100 000 refused records must not wait for the consumer");
        _filter.Lost.ShouldBe(100_000);
    }

    private static FileIoRecord Record(FileIoOperation operation, string path, int pid = Writer, uint disposition = 0, int infoClass = 0) =>
        new(operation, path, pid, DateTime.UtcNow, disposition, infoClass);

    private List<FileIoRecord> Passed()
    {
        var passed = new List<FileIoRecord>();
        while (_filter.Reader.TryRead(out FileIoRecord record))
        {
            passed.Add(record);
        }

        return passed;
    }
}
