using System.Text.Json;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Communication;

/// <summary>
/// The bytes the agent signs are the bytes the GRID verifies.
///
/// <para>
/// The GRID checks an uploaded audit entry with
/// <c>json.dumps(payload, sort_keys=True, separators=(',', ':'), ensure_ascii=False)</c>. If the
/// agent's canonical form differs by one space, one key order or one escaped character, every
/// entry is rejected as forged and the console's Audit log page stays empty -- which is exactly
/// what the owner saw on 2026-10-27.
/// </para>
///
/// <para>
/// The expected strings below were produced by running that Python expression, not by running
/// the C#: a test that asserts the code agrees with itself would have passed on the broken
/// version too.
/// </para>
/// </summary>
public sealed class AuditUploadPayloadTests
{
    private static AuditLog Entry(string action = "SentinelProcessResumed", string? previousHash = "a3f1",
        string? entityType = "CanaryAlert", Guid? entityId = null) => new()
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Sequence = 7,
            Action = action,
            Details = "Process resumed on request: powershell (PID 8900)",
            EntityType = entityType,
            EntityId = entityId ?? Guid.Parse("7f1d2c3b-4a5e-6789-0abc-def012345678"),
            PreviousHash = previousHash,
            CurrentHash = "b4e2",
            Signature = "ignored: the local signature covers different bytes",
            CreatedAt = new DateTime(2026, 10, 27, 5, 40, 9, DateTimeKind.Utc).AddTicks(1234567),
        };

    [Fact]
    public void The_canonical_json_is_byte_for_byte_what_the_GRID_verifies()
    {
        // python -c "import json; print(json.dumps(payload, sort_keys=True,
        //            separators=(',',':'), ensure_ascii=False))"
        const string FromPython =
            "{\"action\":\"SentinelProcessResumed\"," +
            "\"created_at\":\"2026-10-27T05:40:09.1234567Z\"," +
            "\"current_hash\":\"b4e2\"," +
            "\"details\":\"Process resumed on request: powershell (PID 8900)\"," +
            "\"entity_id\":\"7f1d2c3b-4a5e-6789-0abc-def012345678\"," +
            "\"entity_type\":\"CanaryAlert\"," +
            "\"previous_hash\":\"a3f1\"," +
            "\"sequence_number\":7}";

        AuditUploadPayload.CanonicalJson(Entry()).ShouldBe(FromPython);
    }

    [Fact]
    public void A_genesis_entry_sends_a_null_previous_hash_rather_than_leaving_the_key_out()
    {
        // python: json.dumps({... "previous_hash": None ...}) -> "previous_hash":null
        string json = AuditUploadPayload.CanonicalJson(Entry(previousHash: null));

        json.ShouldContain("\"previous_hash\":null");
        // A key the agent omits and the GRID includes is a different payload and a failed
        // signature, so every key is always present.
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!
            .Keys.OrderBy(k => k, StringComparer.Ordinal)
            .ShouldBe(["action", "created_at", "current_hash", "details",
                       "entity_id", "entity_type", "previous_hash", "sequence_number"]);
    }

    [Fact]
    public void Non_ascii_text_is_left_as_itself_the_way_ensure_ascii_False_does()
    {
        // The hospital is in Yaoundé. .NET escapes non-ASCII by default (é), Python with
        // ensure_ascii=False does not, and the two sign different bytes.
        AuditLog entry = Entry(action: "AlertRaised");
        AuditLog accented = new()
        {
            Id = entry.Id, Sequence = entry.Sequence, Action = entry.Action,
            Details = "Yaoundé Regional Hospital", EntityType = entry.EntityType,
            EntityId = entry.EntityId, PreviousHash = entry.PreviousHash,
            CurrentHash = entry.CurrentHash, CreatedAt = entry.CreatedAt,
        };

        AuditUploadPayload.CanonicalJson(accented).ShouldContain("\"details\":\"Yaoundé Regional Hospital\"");
    }

    [Fact]
    public void The_payload_sent_is_parsed_from_the_json_that_was_signed()
    {
        AuditLog entry = Entry();

        Dictionary<string, JsonElement> payload = AuditUploadPayload.Payload(entry);

        payload["sequence_number"].GetInt64().ShouldBe(7);
        payload["action"].GetString().ShouldBe("SentinelProcessResumed");
        // Re-serialising the payload must give back the signed string, or the GRID verifies
        // bytes the agent never signed.
        JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).ShouldBe(AuditUploadPayload.CanonicalJson(entry));
    }
}
