# Demo readiness - 2026-10-28 23:54

Verdict: **DEMO READY**

Written by `scripts/demo-ready.ps1`. No password appears here.

```

[1/5] Docker
      Docker is running

[2/5] The GRID
      grid-nginx  Up 2 days
      grid-api  Up 2 hours (healthy)
      grid-mysql  Up 2 days (healthy)
      grid-redis  Up 2 days (healthy)
      grid-notifier  Up 42 hours (healthy)
      the GRID answers over TLS, validated against its own authority

[3/5] The console
      the console answers on http://localhost:5175

[4/5] The workstation
      PC-LAB is already running

      workstation clocks
      PC-LAB              2s behind the GRID   ok

[5/5] The health check
      running scripts/system-check.ps1; this takes several minutes

1. The platform
  PASS               Container grid-nginx
                     Up 2 days
  PASS               Container grid-api
                     Up 2 hours (healthy)
  PASS               Container grid-mysql
                     Up 2 days (healthy)
  PASS               Container grid-redis
                     Up 2 days (healthy)
  PASS               Container grid-notifier
                     Up 42 hours (healthy)

2-5. The GRID, the accounts, the workstations and the installer
  PASS               Workstation clocks match the GRID
                     PC-LAB              2s behind the GRID   ok

  The platform answers
  PASS               GRID answers over validated TLS
                     version 0.8.0
  PASS               Console answers on localhost:5175

  The bench accounts, isolation and roles
  PASS               Sign-in: admin@yde-hospital.cm (tenant_admin)
                     tenant yde-hospital
  PASS               Sign-in: analyst@yde-hospital.cm (security_analyst)
                     tenant yde-hospital
  PASS               Sign-in: auditor@yde-hospital.cm (read_only_auditor)
                     tenant yde-hospital
  PASS               Sign-in: admin@test-clinic.cm (tenant_admin)
                     tenant test-clinic
  PASS               Sign-in: bench-roles@hopital-yaounde.cm (security_analyst)
                     tenant yde-hospital
  PASS               One customer sees nothing of another
                     test-clinic: 0 workstations, 0 alerts; the hospital: 1 workstation(s)
  PASS               The auditor can read but not act
                     reads alerts (200), refused creating a user (403)

  The workstations and what each module reports
  PASS               Workstations as the GRID reports them
                     1 enrolled (decommissioned ones hidden)
  PASS                 PC-LAB: active, protection partial
                     reasons: EXFIL_WATCH:degraded:learning_phase
  PASS                   PC-LAB / SENTINEL: active
  PASS                   PC-LAB / ENTROPY: active
  PASS                   PC-LAB / GENEALOGY: active
  PASS                   PC-LAB / USB_GUARD: active
  NOT TESTABLE HERE      PC-LAB / EXFIL_WATCH: degraded (learning_phase)
                     it learns the normal traffic before it can judge any
  PASS                 PC-LAB: no alert was refused

  The installer the console hands out
  PASS               An enrollment code is issued
                     valid 15 minutes, single use
  PASS               The console can hand out an installer
                     RansomGuard-CM-Installer-192.168.56.1.exe, 94.3 MB, valid PE header, sha256 F43B824A584C...

  Not built, and not testable on this bench
  NOT BUILT          Anti-tampering (watchdog)
                     the services are registered but never started. The agent stopped reporting it as a module on 2026-10-26: a permanently grey row saying a thing does not exist is noise in a console. It is listed here, and as planned work in SYSTEM_STATUS
  NOT BUILT          A client certificate per agent
                     agents authenticate with an enrollment token, not a per-agent certificate
  NOT BUILT          A signed installer
                     Windows shows 'Publisher: Unknown' on the UAC prompt
  NOT BUILT          IRONCLAD against real hardware
                     the code exists and is tested against a simulated Arduino only
  NOT TESTABLE HERE  USB GUARD against a real key
                     needs a USB device passed through to the VM; the module itself reports active
  NOT TESTABLE HERE  EXFIL WATCH raising an alert
                     it learns the normal traffic first and reports learning_phase until it has a baseline
  NOT TESTABLE HERE  The deployed server
                     OPS-001: the Tailscale server is not touched from here

6. The automated suites
  (several minutes)
  PASS               GRID suite (pytest)
                     447 passed, 1 skipped in 605.25s (0:10:05)
  PASS               Console suite (vitest)
                     [2m      Tests [22m [1m[32m269 passed[39m[22m[90m (269)[39m
  PASS               No French reaches a user
                     no French reaches a user
  PASS               Agent suite (CI, cannot run here)
                     CI run 40781882875 green on 53fbed1
  PASS               Console end-to-end (Playwright)
                     83 passed (2.3m)

------------------------------------------------------------
 SUMMARY
------------------------------------------------------------
  PASS               29
  FAIL               0
  NOT BUILT          4
  NOT TESTABLE HERE  4

  Everything that could be checked, works.
  NOT BUILT and NOT TESTABLE HERE above are the honest state, not failures.

  Report: C:\Users\Fabiola\source\repos\ransomguard-cm\docs\testing\system-check-2026-10-28.md

      PASS               29
      FAIL               0
      NOT BUILT          4
      NOT TESTABLE HERE  4

============================================================
  DEMO READY
============================================================

  Where things are
    console            http://localhost:5175
    GRID               https://192.168.56.1:8443
    hospital           organization code yde-hospital
                       admin@yde-hospital.cm      (can do everything)
                       analyst@yde-hospital.cm    (handles alerts)
                       auditor@yde-hospital.cm    (reads, never acts)
    the other customer organization code test-clinic
                       admin@test-clinic.cm       (sees none of the hospital)

    passwords          C:\Users\Fabiola\source\repos\ransomguard-cm\deployment\bench\credentials.local.md
                       (that file stays on this machine; it is never committed)

    the owner guide    docs/OWNER_TEST_GUIDE.md
```
