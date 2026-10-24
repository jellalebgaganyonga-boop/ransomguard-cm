# RansomGuard-Installer — the double-click installer (ADR-027)

A self-contained single-file executable (the **stub**, published by the CD as
`RansomGuard-Installer-stub.exe`) to which the GRID appends, on every download from the console,
a **payload**: the agent package, the enrollment code, the GRID address and the GRID certificate
authority's public certificate. The stub reads its own payload at run time.

## Payload layout (format `RGINST01`)

From the start of the file:

```
[stub bytes][zip: RansomGuard-Agent-win-x64.zip][config: UTF-8 JSON][trailer, 56 bytes]
```

Trailer, little-endian:

| Offset | Size | Content |
|---|---|---|
| 0 | 8 | ASCII `RGINST01` |
| 8 | 8 | zip length (int64) |
| 16 | 8 | config length (int64, at most 65 536) |
| 24 | 32 | SHA-256 of `zip ‖ config` |

Config JSON (snake_case): `grid_base_url` (required), `otp` (required), `otp_expires_at`
(ISO-8601, UTC), `tenant_code`, `agent_package_sha256` (hex), `grid_ca_pem`.

A .NET single-file host finds its bundle by an offset written at publish time: bytes appended
after it never disturb it. The CI proves it on the published stub: payload appended, the
executable starts and prints its payload (`--verify-payload`, exit 0); the bare stub exits 2.

## Commands

| Command | Does | Exit |
|---|---|---|
| (double-click, no argument) | reads the payload; installs the agent (from step 3) | 0, 2, 3, 4 |
| `--verify-payload` | reads and prints the payload, masks the code | 0 / 2 / 3 |
| `--version` | the stub's version | 0 |
| `--append <stub> <zip> <config.json> <out>` | builds an installer, as the GRID does (dev and CI) | 0 / 64 |

Exit codes: 0 done · 2 payload absent or corrupt (download again) · 3 enrollment code expired ·
4 the agent did not enroll · 64 bad usage.

## Not signed

The executable carries no Authenticode signature: SmartScreen shows « Windows a protégé votre
ordinateur / Éditeur inconnu ». « Informations complémentaires » → « Exécuter quand même », then
« Oui » to the administrator prompt. The signature is debt AGT-INST-002.
