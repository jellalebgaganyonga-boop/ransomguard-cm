# Sprint 3 Manual Tests

## Test 1 — Legitimate File Modification (No False Positive)
1. Open any .docx in Word
2. Add 2-3 paragraphs of text
3. Save
4. Wait 10 seconds
5. **Expected:** ZERO entropy alerts (entropy delta below threshold)

## Test 2 — Simulated Encryption
1. Copy a .docx file to .docx.original
2. Overwrite the .docx with 100 KB cryptographic random bytes:
```powershell
$bytes = New-Object byte[] 102400
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
[System.IO.File]::WriteAllBytes("test.docx", $bytes)
```
3. **Expected:** Entropy Rule 1 or Rule 2 alert within 1 second, severity High/Critical
4. **Expected:** Alert enriched with GENEALOGY (powershell.exe identified)

## Test 3 — Mass Encryption (Directory Shift Rule 3)
1. Create test directory with 10 plaintext files
2. Wait for baseline build (10 seconds)
3. Run script that encrypts all 10 files within 30 seconds
4. **Expected:** Entropy Rule 3 (directory shift) alert fires
5. **Expected:** Severity Critical

## Test 4 — Whitelist Verification
1. Create a .zip file in a watched directory
2. Wait 10 seconds
3. **Expected:** No entropy alert (extension whitelisted)

## Test 5 — SENTINEL + ENTROPY Combined
1. Place SENTINEL canary + real file in same directory
2. Encrypt both
3. **Expected:** SENTINEL alert on canary + ENTROPY alert on real file (both fire)
