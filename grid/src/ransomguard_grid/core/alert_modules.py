"""Which agent module raised an alert, derived from its alert_type.

The agent composes alert_type as <module prefix><event> (AlertMapper): "SentinelCanaryModified"
carries two pieces of information, the module (SENTINEL) and the event (canary modified). The
server exposes the module as a stable code next to alert_type, which it keeps for the event;
the console translates the code (FR/EN). The mapping lives here, on the server, so every
consumer gets the same answer.

Exhaustiveness is tested against shared/contracts/agent-alert-types.json, which the agent's own
test keeps equal to what the agent can send. A type with no module maps to UNKNOWN -- an
explicit code, never an empty cell.
"""

from typing import Final

UNKNOWN: Final = "UNKNOWN"

# Every module code the console may receive (GENEALOGY enriches alerts but raises none).
MODULES: Final = ("SENTINEL", "ENTROPY", "GENEALOGY", "USB_GUARD", "EXFIL_WATCH", UNKNOWN)

# alert_type prefix -> module code
_PREFIXES: Final = (
    ("Sentinel", "SENTINEL"),
    ("Entropy", "ENTROPY"),
    ("UsbGuard", "USB_GUARD"),
    ("ExfilWatch", "EXFIL_WATCH"),
)


def module_for(alert_type: str) -> str:
    """Module code of an alert_type, or UNKNOWN when no module prefix matches."""
    for prefix, module in _PREFIXES:
        if alert_type.startswith(prefix):
            return module
    return UNKNOWN
