# Demonstration script — RansomGuard-CM

**Written for: the jury at the Phase Two defence, and anyone presenting the system to a hospital.**

> *Phase Two: Design and Implementation of a Verifiable Protection Layer (Endpoint Posture,
> Tamper-Evident Audit, and Attack Attribution) for Ransomware Defence in Cameroonian Hospitals.*

This is what to show, in what order, what each step proves about the title, and — just as
important — what to say when something does not work, because every limit below is one you will
be asked about.

Total running time: **about 28 minutes** for the full script, **12 minutes** for the short path
(steps 1, 3, 4, 5, 7). Much of the waiting is deliberate: the agent asks the GRID for commands on
a schedule, so a resume takes up to a minute to arrive. Say so out loud; it is a design decision,
not a delay.

---

## Before anybody is watching

```powershell
pwsh scripts/demo-ready.ps1
```

It starts whatever is not running, then prints **DEMO READY** or **NOT READY** with every problem
and what to do about it. It checks, among other things, the two things that have actually broken
demonstrations here: a workstation clock more than two minutes from the GRID's, and a disk with
no room left.

Have open, before you start:

| | |
|---|---|
| The console | http://localhost:5175 |
| The hospital | organization `yde-hospital`, `admin@yde-hospital.cm` |
| The clinic | organization `test-clinic`, `admin@test-clinic.cm` |
| The workstations | **PC-LAB** (the hospital's) and **PC-RECEPTION** (the clinic's) |

Passwords are in `deployment/bench/credentials.local.md`, which never leaves the machine. Do not
put them on a slide.

---

## 1. The claim, and the shape of the system — 2 min

Say what the three pieces are before showing anything:

- **The agent** runs on a hospital workstation. It watches, it decides, it reports.
- **The GRID** is the server the hospital's IT keeps. Agents report to it over mutual TLS; it
  never holds an agent's signing key.
- **The console** is what a person uses. It shows what the agent said, and nothing it made up.

Then state the claim the title makes: *every answer this system gives can be checked.* The rest
of the demonstration is that sentence, four times.

---

## 2. Endpoint posture — "is this machine protected right now?" — 3 min

**Show:** console → **Agents** → **PC-LAB**.

Point at the verdict line: **Partial protection — EXFIL WATCH degraded: learning phase**, and the
module table beneath it: SENTINEL active, ENTROPY active, GENEALOGY active, USB GUARD active,
EXFIL WATCH degraded.

**Say:** the console is not guessing. Every one of those lines is the workstation's own report,
sent in its heartbeat; the console repeats it and adds nothing. That is why it says *partial* and
names the reason rather than showing a green tick. A dashboard that always says "protected" is
worth nothing to a hospital — the useful question is *which* part is not covered, and why.

**Also point at:** the heartbeat age, the agent version, and — since the clock work — the
workstation's own clock measured against the GRID's.

---

## 3. Attack attribution — "who did it?" — 6 min

**On PC-LAB:** open the disc (`D:`) and run `trigger-sentinel.cmd`. It writes to a decoy file in
`Desktop\RansomGuard-TestZone`. Say plainly: *this is a harmless script that writes to a decoy, not
ransomware. Nothing is encrypted.*

Watch the trigger print `write 1/5 done`, and then stop.

**On the console:** the alert appears within seconds. Open the one that says **Process suspended
after writing a canary**.

Walk the jury down that page:

| What it shows | What it proves |
|---|---|
| **What happened** — the decoy and the process | the detection |
| **Who did it** — `powershell — PID 3620`, its image path and start time | attribution, as a (PID, creation time) pair |
| **What started it** — `explorer → cmd → powershell` | genealogy: the chain, not just the last process |
| **Response** — *suspended, waiting for your decision* | the agent stopped it and then stopped |

**Say the hard part out loud:** the agent will not name a process it cannot prove. If two
processes wrote in the window it says *ambiguous*; if the PID now belongs to a younger process it
says *the PID was reused*; if no write event reached it, it says so. A wrong name is worse than no
name, because somebody acts on it.

---

## 4. The decision belongs to a person — 3 min

Still on that alert, click **Resume the process**, confirm, and go back to PC-LAB.

The trigger continues: `write 2/5 done`, with the gap visible in the timestamps.

**Say:** the software froze the process and then waited. A ransomware tool that decides on its own
to kill processes in a hospital will, one day, kill the wrong one — in the middle of a
consultation. So the agent suspends, which is reversible, and a named administrator decides. The
command and the agent's own answer are both recorded.

---

## 5. Multi-tenancy — "one server, two hospitals, no leakage" — 4 min

This is the SaaS claim, and it is the easiest one to show.

**Show, side by side:**

| Sign in as | Organization | What the console shows |
|---|---|---|
| `admin@yde-hospital.cm` | `yde-hospital` | **1 endpoint: PC-LAB**, and the hospital's alerts |
| `admin@test-clinic.cm` | `test-clinic` | **1 endpoint: PC-RECEPTION**, and only its own alerts |

Neither sees the other's workstation, neither sees the other's alerts, and the counts on the two
dashboards are different numbers from the same server.

**Then show the strong version:** take the agent id from one console and ask for it as the other
customer. The answer is **404 Not Found**, not 403 Forbidden. Say why that matters: 403 would
confirm the thing exists. A customer must not learn even that.

**Where this came from:** PC-RECEPTION was wiped, and its agent was downloaded from the *clinic's*
console with the clinic's own enrolment code. The installer prints the organization it belongs
to — `Organization: test-clinic` — before it installs anything. A workstation belongs to the
tenant whose code enrolled it, and nothing else decides that.

**A detail worth showing if the jury is interested:** the clinic's console is in **French** and
the hospital's in **English**, because the language belongs to the account, not to the build.

---

## 6. Tamper-evident audit — "can the record be trusted?" — 4 min

**Show:** console → **Audit log** (the agent's chain) and **User actions** (who did what in the
console).

**Say what is underneath it:** every entry the agent writes carries a sequence number, the hash of
the entry before it, and its own hash. Then the agent signs it with a key **generated on the
workstation**, whose private half the GRID has never seen and cannot ask for. When the entries are
uploaded, the GRID re-verifies every signature and refuses a gap or a repeat in the sequence.

**The honest framing:** an attacker with administrator rights on that workstation can delete the
agent's database — nothing on a compromised machine can prevent that. What they cannot do is
*rewrite history and have it accepted*: the chain breaks, the signature fails, and the server's
copy disagrees with the machine's. The property is tamper-**evident**, not tamper-proof, and the
difference is the honest claim.

---

## 7. USB GUARD on a real key — 3 min

Plug in an ordinary USB key.

**Show:** nothing happens. That is the demonstration.

**Say:** this module used to call an ordinary key Critical — twice. First on the boot signature
that every formatted volume has; then on entropy, because a PDF and a Word file are compressed
and compressed bytes look like encrypted bytes. In the shipped mode a Critical finding makes the
drive read-only, so a nurse's key would have been locked by the thing meant to protect her. It now
recognises the formats that are compressed by design by their **magic bytes**, not their
extension — a renamed PDF is still forgiven, and a file that only *claims* to be a PDF is not.

On the bench, with the owner's own key: *2 files scanned, 2 skipped, 0 findings, severity None*,
the drive still writable, and the same byte count before and after.

If you want to show it catching something, a key with `autorun.inf` and an executable still
reaches **High**. Do not invent anything more dramatic than that.

---

## 8. What happens when the network goes — 4 min

This is the step that answers "so it only works while the server is up?". It does not, and this
shows why in under four minutes.

**Cut the workstation off**, from the host — the only step in the whole demonstration that needs a
command outside the console:

```powershell
VBoxManage controlvm PC-LAB setlinkstate1 off
VBoxManage controlvm PC-LAB setlinkstate2 off
```

Say what you have just done: the workstation can no longer reach the GRID at all. A hospital in
Yaoundé loses its link often enough that this is not a thought experiment.

**Now attack it anyway.** Run the trigger on the workstation. It writes once and stops:

```
07:20:23  write 1/5 done
```

**That pause is the point.** The decoy was written, the writer was named, and it was suspended —
by the agent, on the machine, with nobody to ask. Detection and response live on the workstation;
the GRID is where the evidence goes, not where the decision is made.

**Show the console** while the link is still down: the alert count has not moved. The hospital
cannot see what it has not been told.

**Put the link back:**

```powershell
VBoxManage controlvm PC-LAB setlinkstate1 on
VBoxManage controlvm PC-LAB setlinkstate2 on
```

Within about a minute the alerts appear — and this is the line to read aloud:

| | detected | arrived |
|---|---|---|
| `SentinelCanaryModified` | **06:20:23** | 06:27:43 |
| `SentinelProcessSuspended` | **06:20:25** | 06:27:43 |

**Seven minutes apart.** The alert carries the time it *happened*, taken from the workstation's
own clock, not the time it was *delivered*. An incident report that said 06:27 would be wrong
about when the hospital was attacked, and that is the kind of error nobody catches afterwards.

Finish by resuming the process from the alert, exactly as in step 4.

**If you have five more minutes**, leave the link down and wait: the console marks the workstation
*signal lost*, then **disconnected**, about six minutes after it goes quiet. That patience is
deliberate — a brief hiccup in a hospital network must not raise an alarm.

Run on 2026-10-29 on PC-LAB; screenshots in `docs/screenshots/real/offline-2026-10-29/`.

---

## The limits — say these before you are asked

Being able to state these clearly is worth more than any feature.

| Limit | Why it is like that | What would close it |
|---|---|---|
| **Anti-tampering is not built.** An administrator can stop the agent and nothing notices. | It was scoped and not implemented; it is listed as not built rather than hidden behind a row that says "inactive". | A protected service, a watchdog, and a GRID-side alarm on a missing heartbeat that distinguishes "stopped" from "off". |
| **The installer is unsigned** — every install shows *Publisher: Unknown*. | No code-signing certificate was bought for a student project. | An EV certificate; nothing in the code changes. |
| **EXFIL WATCH has never raised an alert here.** | It learns normal traffic first and refuses to guess before it has a baseline; the bench has no normal traffic to learn. | A workstation in real use for days. |
| **IRONCLAD (offline backup) has never seen real hardware.** | Tested against a simulated Arduino only. | The actual device. |
| **The agent is Windows-only.** | Its detection sits on ETW and the Windows service model. | A different implementation per platform; the GRID and console are unaffected. |
| **Alert text is English even in the French console.** | The sentence is the agent's own words, recorded as raised; translating stored evidence after the fact would be rewriting it. | Structured alert fields the console renders in either language. |
| **A bench VM's clock free-runs** (no Guest Additions), so it must be set by hand. | They are test machines, not managed endpoints. | Guest Additions, or a domain with time sync — a real hospital has both. |
| **Windows on the bench VMs is not activated**, which is the company the one bug check we saw keeps. | Test machines. | Activate them. |
| **One bug check in a week**: `DRIVER_PORTION_MUST_BE_NONPAGED` in `clipsp.sys`, Windows' own licensing driver, under memory pressure. | 4 GB of RAM and a 0.69 GB page file. | Already done: 6 GB and a fixed 4 GB page file on each VM. |

---

## If something goes wrong in front of the jury

| Symptom | What it is | What to do |
|---|---|---|
| The console does not load | the GRID is not up | `pwsh scripts/demo-ready.ps1` — it starts everything and says what is wrong |
| The workstation is missing from the console | it has not enrolled, or it is in the *other* tenant | check which organization you signed in as; check **Agents** with *All* |
| An alert says *unavailable — pid_reused* | the clock moved while the agent ran, or the PID really was recycled | say what the reason code means; it is the agent refusing to guess |
| The alert is hours old | the workstation's clock has drifted | `deployment/bench/set-clock.ps1`; `demo-ready` now refuses a demo in this state |
| Resume seems to do nothing | the agent asks for commands on a schedule | wait up to a minute; the alert shows *sent, waiting for the agent* |
| The trigger writes once and stops | **that is the point** — it was suspended | show the console, then resume |

---

## The sentence to end on

Three questions a hospital asks after an incident — *was that machine protected, can I trust the
record, and who did it* — and this system answers each one with something you can check, or says
plainly that it does not know. The last part is the design.
