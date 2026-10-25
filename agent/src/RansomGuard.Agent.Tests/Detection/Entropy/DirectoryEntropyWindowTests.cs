using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Entropy;

/// <summary>
/// The counter rule 3 never had (AGT-EN-002). What it must get right:
/// one file rewritten many times is never mass encryption; many files in one folder within the
/// window is; and a burst raises one folder-wide alert, not one per file.
/// </summary>
public sealed class DirectoryEntropyWindowTests
{
    private const string Dir = @"C:\Users\vboxuser\Documents";
    private const int WindowSeconds = 60;

    private static string File(int n) => Path.Combine(Dir, $"patient_{n}.docx");

    private static (DirectoryEntropyWindow Window, ManualTimeProvider Time) Build()
    {
        var time = new ManualTimeProvider();
        return (new DirectoryEntropyWindow(WindowSeconds, time), time);
    }

    [Fact]
    public void One_file_rewritten_many_times_is_one_file()
    {
        // A noisy application saving the same document in a loop must never reach the threshold:
        // mass encryption is about breadth, not repetition.
        (DirectoryEntropyWindow window, ManualTimeProvider time) = Build();

        DirectorySnapshot snapshot = default;
        for (int i = 0; i < 20; i++)
        {
            snapshot = window.Record(File(1), delta: 3.5, writerPid: null);
            time.Advance(TimeSpan.FromSeconds(1));
        }

        snapshot.FileCount.ShouldBe(1);
    }

    [Fact]
    public void Different_files_in_the_same_folder_accumulate()
    {
        (DirectoryEntropyWindow window, ManualTimeProvider time) = Build();

        DirectorySnapshot snapshot = default;
        for (int i = 1; i <= 7; i++)
        {
            snapshot = window.Record(File(i), delta: 3.0, writerPid: null);
            time.Advance(TimeSpan.FromSeconds(2));
        }

        snapshot.FileCount.ShouldBe(7);
        snapshot.AverageDelta.ShouldBe(3.0, 0.001);
    }

    [Fact]
    public void Files_in_different_folders_do_not_add_up()
    {
        (DirectoryEntropyWindow window, _) = Build();

        window.Record(Path.Combine(@"C:\A", "one.docx"), 3.0, null);
        window.Record(Path.Combine(@"C:\B", "two.docx"), 3.0, null);
        DirectorySnapshot third = window.Record(Path.Combine(@"C:\A", "three.docx"), 3.0, null);

        third.FileCount.ShouldBe(2); // the two in C:\A, not the one in C:\B
    }

    [Fact]
    public void Entries_older_than_the_window_stop_counting()
    {
        (DirectoryEntropyWindow window, ManualTimeProvider time) = Build();

        window.Record(File(1), 3.0, null);
        window.Record(File(2), 3.0, null);
        time.Advance(TimeSpan.FromSeconds(WindowSeconds + 5));

        DirectorySnapshot snapshot = window.Record(File(3), 3.0, null);

        snapshot.FileCount.ShouldBe(1); // only the fresh one
    }

    [Fact]
    public void A_burst_is_reported_once_not_once_per_file()
    {
        // Fifty encrypted files must raise one Critical naming the folder. Fifty Criticals would
        // bury the one that matters.
        (DirectoryEntropyWindow window, ManualTimeProvider time) = Build();

        for (int i = 1; i <= 5; i++)
        {
            window.Record(File(i), 3.0, null);
        }

        window.MarkFired(File(5)); // the monitor fired rule 3 here

        int firedAgain = 0;
        for (int i = 6; i <= 50; i++)
        {
            DirectorySnapshot snapshot = window.Record(File(i), 3.0, null);
            if (!snapshot.InCooldown)
            {
                firedAgain++;
            }

            time.Advance(TimeSpan.FromMilliseconds(200));
        }

        firedAgain.ShouldBe(0);
    }

    [Fact]
    public void A_second_wave_after_the_cooldown_is_reported_again()
    {
        (DirectoryEntropyWindow window, ManualTimeProvider time) = Build();

        window.Record(File(1), 3.0, null);
        window.MarkFired(File(1));
        window.Record(File(2), 3.0, null).InCooldown.ShouldBeTrue();

        time.Advance(TimeSpan.FromSeconds(WindowSeconds + 1));

        window.Record(File(3), 3.0, null).InCooldown.ShouldBeFalse();
    }

    [Fact]
    public void One_writer_across_every_attributed_file_is_unanimous()
    {
        (DirectoryEntropyWindow window, _) = Build();

        window.Record(File(1), 3.0, writerPid: 4521);
        window.Record(File(2), 3.0, writerPid: 4521);
        DirectorySnapshot snapshot = window.Record(File(3), 3.0, writerPid: 4521);

        snapshot.UnanimousWriterPid.ShouldBe(4521);
    }

    [Fact]
    public void A_file_nobody_could_attribute_does_not_break_unanimity()
    {
        // Attribution fails often (no ETW, PID 4, a reused PID). A file with no writer must not
        // stop the folder-wide alert from naming the one process that did write the others.
        (DirectoryEntropyWindow window, _) = Build();

        window.Record(File(1), 3.0, writerPid: 4521);
        window.Record(File(2), 3.0, writerPid: null);
        DirectorySnapshot snapshot = window.Record(File(3), 3.0, writerPid: 4521);

        snapshot.UnanimousWriterPid.ShouldBe(4521);
    }

    [Fact]
    public void Two_different_writers_name_nobody()
    {
        (DirectoryEntropyWindow window, _) = Build();

        window.Record(File(1), 3.0, writerPid: 4521);
        DirectorySnapshot snapshot = window.Record(File(2), 3.0, writerPid: 7777);

        snapshot.UnanimousWriterPid.ShouldBeNull();
    }

    [Fact]
    public void No_attribution_at_all_names_nobody()
    {
        (DirectoryEntropyWindow window, _) = Build();

        window.Record(File(1), 3.0, null);
        DirectorySnapshot snapshot = window.Record(File(2), 3.0, null);

        snapshot.UnanimousWriterPid.ShouldBeNull();
    }

    [Fact]
    public void A_file_seen_twice_keeps_the_worse_rise()
    {
        // The average must follow the worst observation of a file, not the first one.
        (DirectoryEntropyWindow window, _) = Build();

        window.Record(File(1), delta: 1.0, writerPid: null);
        DirectorySnapshot snapshot = window.Record(File(1), delta: 4.0, writerPid: null);

        snapshot.FileCount.ShouldBe(1);
        snapshot.AverageDelta.ShouldBe(4.0, 0.001);
    }

    [Fact]
    public void The_window_stays_bounded_across_many_folders()
    {
        (DirectoryEntropyWindow window, ManualTimeProvider time) = Build();

        for (int i = 0; i < DirectoryEntropyWindow.MaxDirectories + 500; i++)
        {
            window.Record(Path.Combine($@"C:\dir{i}", "f.docx"), 3.0, null);
            time.Advance(TimeSpan.FromMilliseconds(10));
        }

        window.TrackedDirectories.ShouldBeLessThanOrEqualTo(DirectoryEntropyWindow.MaxDirectories);
    }

    [Fact]
    public void One_folder_cannot_grow_without_bound()
    {
        (DirectoryEntropyWindow window, _) = Build();

        DirectorySnapshot snapshot = default;
        for (int i = 0; i < DirectoryEntropyWindow.MaxFilesPerDirectory + 200; i++)
        {
            snapshot = window.Record(File(i), 3.0, null);
        }

        snapshot.FileCount.ShouldBeLessThanOrEqualTo(DirectoryEntropyWindow.MaxFilesPerDirectory);
    }

    [Fact]
    public void The_folder_is_the_one_the_alert_names()
    {
        DirectoryEntropyWindow.DirectoryOf(File(1))
            .ShouldBe(Dir, StringCompareShould.IgnoreCase);
    }
}
