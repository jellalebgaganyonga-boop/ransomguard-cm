namespace RansomGuard.Agent.Core.Detection.IronClad.Models;

/// <summary>
/// State of a physical USB port relay managed by the IronClad controller.
/// </summary>
public enum PortState
{
    /// <summary>Port is powered and operational.</summary>
    Active,

    /// <summary>Port power has been cut by the relay controller.</summary>
    Cut,

    /// <summary>Port state could not be determined.</summary>
    Unknown,

    /// <summary>Port relay has reported a hardware fault.</summary>
    Faulted
}
