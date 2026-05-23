using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.ExfilWatch;

namespace RansomGuard.Agent.Core.Detection.ThreatIntel;

/// <summary>
/// Loads threat intel data from embedded JSON resources or an external directory.
/// Implements <see cref="IThreatIntelProvider"/> with HashSet-based O(1) lookups.
/// </summary>
public sealed class ThreatIntelDataLoader : IThreatIntelProvider
{
    private readonly ILogger<ThreatIntelDataLoader> _logger;
    private readonly object _reloadLock = new();

    private HashSet<string> _torExitNodes = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _knownC2Servers = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _whitelistedDomains = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _lolbasBinaries = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _cloudPrefixes = new();
    private string _version = "1.0.0";

    /// <inheritdoc />
    public string Version => _version;

    /// <summary>Number of Tor exit nodes loaded.</summary>
    public int TorExitNodeCount => _torExitNodes.Count;

    /// <summary>Number of C2 servers loaded.</summary>
    public int C2ServerCount => _knownC2Servers.Count;

    /// <summary>Number of LOLBAS binaries loaded.</summary>
    public int LolbasBinaryCount => _lolbasBinaries.Count;

    /// <summary>Number of whitelisted domains loaded.</summary>
    public int WhitelistedDomainCount => _whitelistedDomains.Count;

    /// <summary>Initializes the provider and loads embedded data.</summary>
    public ThreatIntelDataLoader(ILogger<ThreatIntelDataLoader> logger)
    {
        _logger = logger;
        LoadFromEmbeddedResources();
    }

    /// <inheritdoc />
    public bool IsWhitelistedDestination(string remoteAddress)
    {
        return _whitelistedDomains.Contains(remoteAddress);
    }

    /// <inheritdoc />
    public bool IsKnownCloudProvider(string remoteAddress)
    {
        foreach (var prefix in _cloudPrefixes)
        {
            if (remoteAddress.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <inheritdoc />
    public bool IsTorExitNode(string remoteAddress)
    {
        return _torExitNodes.Contains(remoteAddress);
    }

    /// <inheritdoc />
    public bool IsKnownC2Server(string remoteAddress)
    {
        return _knownC2Servers.Contains(remoteAddress);
    }

    /// <inheritdoc />
    public bool IsLolbasBinary(string processName)
    {
        return _lolbasBinaries.Contains(processName);
    }

    /// <inheritdoc />
    public void ReloadFromDirectory(string dataDirectory)
    {
        lock (_reloadLock)
        {
            try
            {
                LoadTorExitNodes(Path.Combine(dataDirectory, "tor-exit-nodes.json"));
                LoadC2Servers(Path.Combine(dataDirectory, "known-c2-servers.json"));
                LoadCloudProviders(Path.Combine(dataDirectory, "cloud-providers.json"));
                LoadLolbasBinaries(Path.Combine(dataDirectory, "lolbas-binaries.json"));

                _logger.LogInformation(
                    "Threat intel reloaded from {Dir}: {Tor} Tor nodes, {C2} C2 servers, {Lolbas} LOLBAS, {Whitelist} whitelisted",
                    dataDirectory, _torExitNodes.Count, _knownC2Servers.Count,
                    _lolbasBinaries.Count, _whitelistedDomains.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload threat intel from {Dir}", dataDirectory);
                throw;
            }
        }
    }

    private void LoadFromEmbeddedResources()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = "RansomGuard.Agent.Core.Detection.ThreatIntel.Data.";

        LoadTorExitNodesFromStream(assembly.GetManifestResourceStream(prefix + "tor-exit-nodes.json"));
        LoadC2ServersFromStream(assembly.GetManifestResourceStream(prefix + "known-c2-servers.json"));
        LoadCloudProvidersFromStream(assembly.GetManifestResourceStream(prefix + "cloud-providers.json"));
        LoadLolbasBinariesFromStream(assembly.GetManifestResourceStream(prefix + "lolbas-binaries.json"));

        _logger.LogInformation(
            "Threat intel loaded: {Tor} Tor nodes, {C2} C2 servers, {Lolbas} LOLBAS, {Whitelist} whitelisted",
            _torExitNodes.Count, _knownC2Servers.Count, _lolbasBinaries.Count, _whitelistedDomains.Count);
    }

    private void LoadTorExitNodes(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        LoadTorExitNodesFromStream(stream);
    }

    private void LoadTorExitNodesFromStream(Stream? stream)
    {
        if (stream is null) return;
        var doc = JsonDocument.Parse(stream);
        var entries = doc.RootElement.GetProperty("entries");
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.EnumerateArray())
            set.Add(entry.GetString()!);
        _torExitNodes = set;
        if (doc.RootElement.TryGetProperty("version", out var v))
            _version = v.GetString() ?? _version;
    }

    private void LoadC2Servers(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        LoadC2ServersFromStream(stream);
    }

    private void LoadC2ServersFromStream(Stream? stream)
    {
        if (stream is null) return;
        var doc = JsonDocument.Parse(stream);
        var entries = doc.RootElement.GetProperty("entries");
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.EnumerateArray())
            set.Add(entry.GetString()!);
        _knownC2Servers = set;
    }

    private void LoadCloudProviders(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        LoadCloudProvidersFromStream(stream);
    }

    private void LoadCloudProvidersFromStream(Stream? stream)
    {
        if (stream is null) return;
        var doc = JsonDocument.Parse(stream);

        // Load cloud prefixes
        var prefixes = new List<string>();
        if (doc.RootElement.TryGetProperty("providers", out var providers))
        {
            foreach (var provider in providers.EnumerateObject())
            {
                foreach (var prefix in provider.Value.EnumerateArray())
                    prefixes.Add(prefix.GetString()!);
            }
        }
        _cloudPrefixes = prefixes.Distinct().ToList();

        // Load whitelisted domains
        if (doc.RootElement.TryGetProperty("whitelisted_domains", out var domains))
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var domain in domains.EnumerateArray())
                set.Add(domain.GetString()!);
            _whitelistedDomains = set;
        }
    }

    private void LoadLolbasBinaries(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        LoadLolbasBinariesFromStream(stream);
    }

    private void LoadLolbasBinariesFromStream(Stream? stream)
    {
        if (stream is null) return;
        var doc = JsonDocument.Parse(stream);
        var entries = doc.RootElement.GetProperty("entries");
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.EnumerateArray())
            set.Add(entry.GetString()!);
        _lolbasBinaries = set;
    }
}
