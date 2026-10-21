# EXFIL WATCH — network exfiltration watch

Kernel ETW capture of TCP/IP and DNS activity (administrator rights required: without them the
module reads `inactive / admin_rights_missing`), a sliding-window volume tracker, detection rules,
and a response (alert, throttle, firewall block by severity).

## The rules, exactly (2026-10-21, EXFIL-REG-001)

| Rule | Registered | During the learning phase (7 days) | After |
|---|---|---|---|
| `VolumeAnomaly` (bytes sent vs. the destination's baseline × multiplier) | yes (built in) | **mute**: it returns nothing while `IsLearningPhase`, and needs a baseline that the learning phase builds | fires |
| `DnsTunneling` (queries per PID ≥ threshold, with high-entropy names) | yes (built in) | fires | fires |
| `AfterHoursExfil` (≥ 50 MB in 5 min per PID outside working hours) | yes (built in) | fires | fires |
| `LolbasExfil` (`certutil.exe`, `bitsadmin.exe`, … sending ≥ 1 MB) | yes (DI) | fires | fires |
| `TorTraffic` | **no** | — | — |
| `SuspiciousDestination` | **no** | — | — |
| `UnknownProcessExfil` | **no** | — | — |
| `EncryptedExfilCorrelation` | **no** | — | — |

The four unregistered rules exist as classes and are **not wired**, on purpose and said here
(AGT-EXF-002): `TorTraffic` and `SuspiciousDestination` depend on threat-intelligence lists that
are not fed (the ThreatFox download is disabled since `9b5b7c0`, it answered 401 at every pass); `UnknownProcessExfil` needs a process
baseline the learning phase does not build yet; `EncryptedExfilCorrelation` indexes ENTROPY
signals by PID, and those signals carry PID 0 (AGT-ATTR-001). Registering them without their
inputs would add rules that never fire while the module reads "8 rules".

## The learning phase, honestly

- While learning, the module reads **`degraded / learning_phase`** in the posture: three rules
  fire, `VolumeAnomaly` does not. Once the phase ends, the module re-reads `active` within a minute.
- The phase is measured from the **tracker's creation**, in memory: **every agent restart starts
  the 7 days again** and empties the baselines (AGT-EXF-001). A machine restarted weekly never
  leaves the learning phase. The fix is a persisted baseline; it is not done.

## What "active" means

The module publishes `active` only after resolving, once, every service a network event needs
(`IDataVolumeTracker`, `ExfilRuleEngine`, `IExfilActionEngine`). A missing one is
`inactive / init_failed` with a Critical log -- never a module that reads active while it skips
every rule, which is what happened until 2026-10-21: the tracker was registered nowhere in
production, the consumer resolved it to null and evaluated nothing (EXFIL-REG-001; test
`ExfilWatchProductionWiringTests`, red at run 39584961535 on the unfixed code).

`IDataVolumeTracker` is a **Singleton** on purpose: the monitor opens a scope per event; Scoped,
each event would get an empty tracker and no sliding window would ever fill.
