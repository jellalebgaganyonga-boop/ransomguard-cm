using System.Text;
using System.Text.Json;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Communication;

/// <summary>
/// The form of an audit entry that travels to the GRID, and the exact bytes its signature covers.
/// </summary>
/// <remarks>
/// <para>
/// The agent keeps two signatures over the same entry, and they are not interchangeable.
/// <see cref="AuditLog.Signature"/> covers a packed binary payload and is what proves the local
/// chain has not been edited on the workstation. The GRID verifies something else entirely --
/// <c>json.dumps(payload, sort_keys=True, separators=(',', ':'), ensure_ascii=False)</c> -- so an
/// entry uploaded with its local signature is rejected as forged, every time.
/// </para>
/// <para>
/// That is what was wrong until 2026-10-27: nothing ever uploaded, and had anything tried, the
/// GRID would have refused it. The console's Audit log page read "No entries found" for a chain
/// the workstations had been writing correctly all along.
/// </para>
/// <para>
/// So the canonical JSON is built here, once, and both the signature and the uploaded payload
/// come from it. They cannot drift apart: the signature is over <see cref="CanonicalJson"/> and
/// the payload parsed from the same string.
/// </para>
/// </remarks>
public static class AuditUploadPayload
{
    /// <summary>
    /// Matches Python's canonical form: keys sorted, no spaces, UTF-8 kept as UTF-8.
    /// </summary>
    private static readonly JsonSerializerOptions Canonical = new()
    {
        WriteIndented = false,
        // Python's ensure_ascii=False leaves non-ASCII as itself; .NET escapes it by default,
        // and the hospital's own name, signed one way and verified the other, does not come back
        // equal. The accented spelling is exercised in AuditUploadPayloadTests.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The canonical JSON for one chained entry: the bytes the GRID verifies the signature over.
    /// </summary>
    /// <remarks>
    /// The keys are written in sorted order by hand rather than by a comparer, because the order
    /// is part of the wire contract and a reader should be able to see it.
    /// </remarks>
    public static string CanonicalJson(AuditLog entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var ordered = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["action"] = entry.Action,
            ["created_at"] = entry.CreatedAt.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            ["current_hash"] = entry.CurrentHash,
            ["details"] = entry.Details,
            ["entity_id"] = entry.EntityId?.ToString(),
            ["entity_type"] = entry.EntityType,
            ["previous_hash"] = entry.PreviousHash,
            ["sequence_number"] = entry.Sequence,
        };

        return JsonSerializer.Serialize(ordered, Canonical);
    }

    /// <summary>The canonical JSON as the bytes that are signed and verified.</summary>
    public static byte[] CanonicalBytes(AuditLog entry) => Encoding.UTF8.GetBytes(CanonicalJson(entry));

    /// <summary>
    /// The payload object to send, parsed back from the very JSON that was signed.
    /// </summary>
    /// <remarks>
    /// Round-tripping rather than building a second dictionary is deliberate: anything that
    /// re-serialises differently from what was signed is a rejected entry, and the only way to
    /// be sure they agree is for there to be one string.
    /// </remarks>
    public static Dictionary<string, JsonElement> Payload(AuditLog entry) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(CanonicalJson(entry))!;
}
