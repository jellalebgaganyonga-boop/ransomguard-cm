namespace RansomGuard.Agent.Core.Security.Cryptography;

/// <summary>
/// Signs an audit entry in the form the GRID verifies, and says which key did it.
/// </summary>
/// <remarks>
/// Only the upload half of <see cref="AuditLogSigner"/> is behind an interface, because only the
/// upload half needs standing in for: the real signer loads an Ed25519 key through Windows DPAPI,
/// which a test cannot do, while what the uploader has to get right is which entries it sends and
/// what it does with them afterwards. The bytes themselves are pinned against the GRID's own
/// verifier in AuditUploadPayloadTests, so nothing about the wire format rests on a stub.
/// </remarks>
public interface IAuditUploadSigner
{
    /// <summary>Which key signed it; stored by the GRID beside every entry.</summary>
    string KeyId { get; }

    /// <summary>Signs the canonical JSON bytes of one entry. Base64.</summary>
    string SignUpload(ReadOnlySpan<byte> canonicalJson);
}
