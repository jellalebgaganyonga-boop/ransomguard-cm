using RansomGuard.Agent.Core.Detection.IronClad.Models;

namespace RansomGuard.Agent.Core.Detection.IronClad.Communication;

/// <summary>
/// Wire protocol for IronClad relay controller communication.
/// Format: CMD:ACTION[:PARAM]\n (agent to device), ACK/ERR/STATUS/VERSION/PONG (device to agent).
/// Uses ReadOnlySpan for allocation-free parsing on the hot path.
/// </summary>
public static class IronCladProtocol
{
    /// <summary>Maximum supported relay port count.</summary>
    public const int MaxPortCount = 16;

    /// <summary>Default relay port count for standard 4-port controller.</summary>
    public const int DefaultPortCount = 4;

    /// <summary>
    /// Serializes a command to wire format for transmission to the device.
    /// </summary>
    public static string Serialize(IronCladCommand command)
    {
        return command.Action switch
        {
            IronCladAction.CutPort => $"CMD:CUT_PORT:{command.Parameter}",
            IronCladAction.RestorePort => $"CMD:RESTORE_PORT:{command.Parameter}",
            IronCladAction.CutAll => "CMD:CUT_ALL",
            IronCladAction.RestoreAll => "CMD:RESTORE_ALL",
            IronCladAction.Status => "CMD:STATUS",
            IronCladAction.Version => "CMD:VERSION",
            IronCladAction.Heartbeat => "CMD:HEARTBEAT",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.Action, "Unknown IronClad action")
        };
    }

    /// <summary>
    /// Parses a raw wire response from the device into a typed response.
    /// </summary>
    public static IronCladResponse Parse(string raw, Guid commandId)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return MakeError(commandId, raw ?? "", "E999", "Empty response");
        }

        var trimmed = raw.Trim();
        var span = trimmed.AsSpan();

        if (span.StartsWith("ACK:"))
        {
            return new IronCladResponse
            {
                CommandId = commandId,
                Type = IronCladResponseType.Ack,
                RawPayload = trimmed,
                ReceivedAt = DateTime.UtcNow,
                IsAcknowledgement = true
            };
        }

        if (span.StartsWith("STATUS:"))
        {
            return new IronCladResponse
            {
                CommandId = commandId,
                Type = IronCladResponseType.Status,
                RawPayload = trimmed,
                ReceivedAt = DateTime.UtcNow,
                IsAcknowledgement = false
            };
        }

        if (span.StartsWith("VERSION:"))
        {
            return new IronCladResponse
            {
                CommandId = commandId,
                Type = IronCladResponseType.Version,
                RawPayload = trimmed,
                ReceivedAt = DateTime.UtcNow,
                IsAcknowledgement = false
            };
        }

        if (span.SequenceEqual("PONG"))
        {
            return new IronCladResponse
            {
                CommandId = commandId,
                Type = IronCladResponseType.Pong,
                RawPayload = trimmed,
                ReceivedAt = DateTime.UtcNow,
                IsAcknowledgement = true
            };
        }

        if (span.StartsWith("ERR:"))
        {
            return ParseError(trimmed, commandId);
        }

        return MakeError(commandId, trimmed, "E999", $"Malformed response: {trimmed}");
    }

    /// <summary>
    /// Parses a STATUS response payload into a port state dictionary.
    /// Format: STATUS:PORT_1_ACTIVE;PORT_2_ACTIVE;PORT_3_CUT;PORT_4_ACTIVE
    /// </summary>
    public static IReadOnlyDictionary<int, PortState> ParsePortStates(string statusPayload)
    {
        var result = new Dictionary<int, PortState>();

        var span = statusPayload.AsSpan();
        if (!span.StartsWith("STATUS:"))
            return result;

        var data = span["STATUS:".Length..];
        foreach (var segment in new SpanSplitEnumerator(data, ';'))
        {
            // Format: PORT_N_STATE
            if (!segment.StartsWith("PORT_"))
                continue;

            var afterPort = segment["PORT_".Length..];
            var underscoreIdx = afterPort.IndexOf('_');
            if (underscoreIdx < 0)
                continue;

            if (!int.TryParse(afterPort[..underscoreIdx], out var portNumber))
                continue;

            var stateStr = afterPort[(underscoreIdx + 1)..];
            var state = stateStr switch
            {
                "ACTIVE" => PortState.Active,
                "CUT" => PortState.Cut,
                "FAULTED" => PortState.Faulted,
                _ => PortState.Unknown
            };

            result[portNumber] = state;
        }

        return result;
    }

    /// <summary>
    /// Extracts the version string from a VERSION response.
    /// </summary>
    public static string ParseVersion(string versionPayload)
    {
        var span = versionPayload.AsSpan();
        if (span.StartsWith("VERSION:"))
            return versionPayload["VERSION:".Length..];
        return versionPayload;
    }

    private static IronCladResponse ParseError(string raw, Guid commandId)
    {
        // Format: ERR:E001:Invalid port number
        var parts = raw.Split(':', 3);
        var errorCode = parts.Length > 1 ? parts[1] : "E999";
        var errorMessage = parts.Length > 2 ? parts[2] : "Unknown error";

        return new IronCladResponse
        {
            CommandId = commandId,
            Type = IronCladResponseType.Error,
            RawPayload = raw,
            ReceivedAt = DateTime.UtcNow,
            IsAcknowledgement = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }

    private static IronCladResponse MakeError(Guid commandId, string raw, string code, string message)
    {
        return new IronCladResponse
        {
            CommandId = commandId,
            Type = IronCladResponseType.Error,
            RawPayload = raw,
            ReceivedAt = DateTime.UtcNow,
            IsAcknowledgement = false,
            ErrorCode = code,
            ErrorMessage = message
        };
    }

    /// <summary>
    /// Allocation-free span splitter for semicolon-delimited status responses.
    /// </summary>
    private ref struct SpanSplitEnumerator
    {
        private ReadOnlySpan<char> _remaining;
        private readonly char _separator;
        private ReadOnlySpan<char> _current;

        public SpanSplitEnumerator(ReadOnlySpan<char> span, char separator)
        {
            _remaining = span;
            _separator = separator;
            _current = default;
        }

        public readonly SpanSplitEnumerator GetEnumerator() => this;

        public ReadOnlySpan<char> Current => _current;

        public bool MoveNext()
        {
            if (_remaining.Length == 0)
                return false;

            var idx = _remaining.IndexOf(_separator);
            if (idx < 0)
            {
                _current = _remaining;
                _remaining = default;
                return true;
            }

            _current = _remaining[..idx];
            _remaining = _remaining[(idx + 1)..];
            return true;
        }
    }
}
