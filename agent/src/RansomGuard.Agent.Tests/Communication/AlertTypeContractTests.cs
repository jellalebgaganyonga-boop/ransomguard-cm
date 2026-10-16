using System.Reflection;
using System.Text.Json;
using Moq;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.Entropy.DetectionRules;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Communication;

/// <summary>
/// The list of alert types the agent can send (shared/contracts/agent-alert-types.json) must be
/// exactly what the code produces. The GRID maps each entry to a module; if a new rule or canary
/// type appeared without updating the contract, the MODULE column would silently show UNKNOWN.
/// </summary>
public sealed class AlertTypeContractTests
{
    private static string ContractPath => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "..",
        "shared", "contracts", "agent-alert-types.json");

    [Fact]
    public void Contract_lists_exactly_the_alert_types_the_agent_can_send()
    {
        using JsonDocument contract = JsonDocument.Parse(File.ReadAllText(ContractPath));
        string[] listed = contract.RootElement.GetProperty("alert_types")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();

        string[] produced = ProducedAlertTypes().ToArray();

        listed.ShouldBeUnique();
        listed.Order().ShouldBe(produced.Order());
    }

    /// <summary>Every alert type AlertMapper produces, derived from the code itself.</summary>
    private static IEnumerable<string> ProducedAlertTypes()
    {
        // SENTINEL: one per canary alert type
        foreach (CanaryAlertType type in Enum.GetValues<CanaryAlertType>())
        {
            yield return AlertMapper.FromCanaryAlert(new CanaryAlert
            {
                CanaryId = Guid.NewGuid(), CanaryPath = @"C:\canary.docx", AlertType = type,
            }).AlertType;
        }

        // ENTROPY: one per entropy rule
        foreach (IEntropyRule rule in Instances<IEntropyRule>())
        {
            yield return AlertMapper.FromEntropyAlert(new EntropyAlert
            {
                FilePath = @"C:\file.docx", RuleId = 1, RuleName = rule.Name, Severity = "High",
                BaselineEntropy = 4.0, CurrentEntropy = 7.9, Delta = 3.9,
            })!.AlertType;
        }

        // USB GUARD: a single type
        yield return AlertMapper.FromUsbAlert(new UsbAlert
        {
            UsbScanResultId = Guid.NewGuid(), Title = "t", Description = "d", Severity = "High", ActionTaken = "AlertOnly",
        }).AlertType;

        // EXFIL WATCH: one per detection rule
        foreach (IExfilDetectionRule rule in Instances<IExfilDetectionRule>())
        {
            yield return AlertMapper.FromExfilFinding(new ExfilFinding
            {
                RuleName = rule.RuleName, Severity = default, Description = "d", ProcessId = 1,
                ProcessName = "p.exe", Destination = "203.0.113.1", BytesTransferred = 1, MitreId = "T1041",
            }).AlertType;
        }
    }

    /// <summary>Instantiates every concrete implementation of <typeparamref name="T"/> in Core.</summary>
    private static IEnumerable<T> Instances<T>() where T : class
    {
        IEnumerable<Type> types = typeof(T).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(t));

        foreach (Type type in types)
        {
            ConstructorInfo ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            object[] args = ctor.GetParameters().Select(p => Argument(p.ParameterType)).ToArray();
            yield return (T)ctor.Invoke(args);
        }
    }

    private static object Argument(Type type)
    {
        if (type == typeof(EntropyOptions))
            return new EntropyOptions();
        if (type.IsInterface)
            return ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!).Object;
        return Activator.CreateInstance(type)!;
    }
}
