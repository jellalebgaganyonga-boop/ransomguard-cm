using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Entropy;

/// <summary>
/// Tests for <see cref="EntropyBaselineService"/>.
/// </summary>
public sealed class EntropyBaselineServiceTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly EntropyBaselineRepository _repository;
    private readonly EntropyBaselineService _service;
    private readonly string _testDir;

    public EntropyBaselineServiceTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _repository = new EntropyBaselineRepository(_context);

        var calculator = new EntropyCalculator(new Mock<ILogger<EntropyCalculator>>().Object);
        _service = new EntropyBaselineService(_repository, calculator,
            new Mock<ILogger<EntropyBaselineService>>().Object);

        _testDir = Path.Combine(Path.GetTempPath(), $"rg_baseline_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task BuildBaselineAsync_NewDirectory_CreatesEntries()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "record1.txt"), "Patient Mballa Jean");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "record2.txt"), "Patient Ngoa Marie");

        await _service.BuildBaselineAsync(_testDir);

        int count = await _repository.CountAsync();
        count.ShouldBe(2);
    }

    [Fact]
    public async Task BuildBaselineAsync_RunTwice_NoDuplicates()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "data.txt"), "Test content");

        await _service.BuildBaselineAsync(_testDir);
        await _service.BuildBaselineAsync(_testDir);

        int count = await _repository.CountAsync();
        count.ShouldBe(1);
    }

    [Fact]
    public async Task BuildBaselineAsync_SkipsCanaryFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "0001_canary.txt"), "Canary content");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "real_file.txt"), "Real content");

        await _service.BuildBaselineAsync(_testDir);

        int count = await _repository.CountAsync();
        count.ShouldBe(1); // Only real file
    }

    [Fact]
    public async Task BuildBaselineAsync_SkipsEmptyFiles()
    {
        await File.WriteAllBytesAsync(Path.Combine(_testDir, "empty.txt"), []);
        await File.WriteAllTextAsync(Path.Combine(_testDir, "notempty.txt"), "Content");

        await _service.BuildBaselineAsync(_testDir);

        int count = await _repository.CountAsync();
        count.ShouldBe(1);
    }

    [Fact]
    public async Task GetBaselineAsync_ReturnsStoredBaseline()
    {
        string path = Path.Combine(_testDir, "patient.txt");
        await File.WriteAllTextAsync(path, "Patient record content here for testing entropy");

        await _service.BuildBaselineAsync(_testDir);

        EntropyBaseline? baseline = await _service.GetBaselineAsync(path);
        baseline.ShouldNotBeNull();
        baseline.EntropyValue.ShouldBeGreaterThan(0);
        baseline.FileExtension.ShouldBe(".txt");
    }

    [Fact]
    public async Task UpdateBaselineAsync_PersistsNewValue()
    {
        string path = Path.Combine(_testDir, "data.txt");
        await File.WriteAllTextAsync(path, "Original content");
        await _service.BuildBaselineAsync(_testDir);

        await _service.UpdateBaselineAsync(path, 7.5, 2048);

        EntropyBaseline? updated = await _service.GetBaselineAsync(path);
        updated.ShouldNotBeNull();
        updated.EntropyValue.ShouldBe(7.5);
    }

    [Fact]
    public async Task GetDirectoryAverageEntropyAsync_ReturnsCorrectMean()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "a.txt"), "Short text");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "b.txt"), "Another short text");

        await _service.BuildBaselineAsync(_testDir);

        double? avg = await _service.GetDirectoryAverageEntropyAsync(_testDir);
        avg.ShouldNotBeNull();
        avg!.Value.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GetDirectoryAverageEntropyAsync_EmptyDirectory_ReturnsNull()
    {
        double? avg = await _service.GetDirectoryAverageEntropyAsync(_testDir);
        avg.ShouldBeNull();
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); } catch { }
    }
}
