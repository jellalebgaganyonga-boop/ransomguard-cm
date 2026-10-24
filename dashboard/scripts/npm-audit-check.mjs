#!/usr/bin/env node
/**
 * Reads `npm audit --json` on stdin and fails if any runtime (production) advisory of severity
 * `high` or `critical` is reported, UNLESS its GHSA is in the specific allowlist below.
 *
 * The allowlist is deliberately per-GHSA — never "by severity" and never a wildcard — so the gate
 * stays strict and each exception has an owner, a reason and a planned fix. Add a GHSA here only
 * after the same identifier appears in docs/security/dependency-risk-register.md with its risk
 * assessment and remediation plan. Remove it when the dependency is upgraded.
 *
 * Why not `npm audit --audit-level=critical` or `--audit-level=none`? Those would hide every
 * future advisory of the same severity, which is exactly what the standing rule forbids.
 */

const ALLOWED = {
  // zod: memory exhaustion while validating oversized constrained collections. Fix is only in
  // zod v4 (breaking change). This console validates its own GRID's responses on a trusted LAN;
  // no attacker-controlled schema input reaches zod. Planned fix: upgrade to zod v4 next sprint,
  // ported schemas and tests. See docs/security/dependency-risk-register.md.
  "GHSA-mrhx-c7xc-m3w5": "zod < 4.6.6 — console on trusted LAN; upgrade planned next sprint",
};

const chunks = [];
process.stdin.on("data", (c) => chunks.push(c));
process.stdin.on("end", () => {
  const text = Buffer.concat(chunks).toString("utf8").trim();
  if (!text) {
    console.error("npm-audit-check: no JSON on stdin");
    process.exit(2);
  }
  let report;
  try {
    report = JSON.parse(text);
  } catch (e) {
    console.error(`npm-audit-check: cannot parse npm audit JSON: ${e.message}`);
    process.exit(2);
  }
  const vulns = report.vulnerabilities ?? {};
  const unexpected = [];
  const allowed = [];
  for (const [name, v] of Object.entries(vulns)) {
    if (v.severity !== "high" && v.severity !== "critical") continue;
    const ghsas = new Set();
    for (const via of v.via ?? []) {
      if (typeof via !== "object" || !via.url) continue;
      const m = via.url.match(/GHSA-[a-z0-9-]+/i);
      if (m) ghsas.add(m[0]);
    }
    const identified = ghsas.size > 0 ? [...ghsas] : ["(no GHSA)"];
    const unknowns = identified.filter((id) => !ALLOWED[id]);
    if (unknowns.length === 0) {
      allowed.push({ name, severity: v.severity, ghsas: identified });
    } else {
      unexpected.push({ name, severity: v.severity, ghsas: unknowns });
    }
  }
  for (const { name, severity, ghsas } of allowed) {
    for (const id of ghsas) {
      console.log(`allowed: ${severity.padEnd(8)} ${name} ${id} — ${ALLOWED[id]}`);
    }
  }
  if (unexpected.length === 0) {
    console.log(`npm audit: 0 unexpected high/critical runtime advisories (${allowed.length} on the allowlist).`);
    process.exit(0);
  }
  console.error("");
  console.error("npm audit: unexpected high/critical runtime advisory. Fix the dependency, or");
  console.error("add the specific GHSA to the allowlist in this script AND to the risk register.");
  for (const { name, severity, ghsas } of unexpected) {
    for (const id of ghsas) {
      console.error(`  - ${severity.padEnd(8)} ${name} ${id}`);
    }
  }
  process.exit(1);
});
