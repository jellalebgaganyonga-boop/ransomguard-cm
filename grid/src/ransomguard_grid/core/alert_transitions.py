"""The closed table of alert status transitions (GRID-ALR-001).

The GRID used to accept any status change -- Resolved back to New, New straight to Resolved, the
same status twice. The console only offers two moves; the server now enforces them:

    New            -> Investigating                          (acknowledge)
    Investigating  -> Resolved | FalsePositive | Suppressed  (close)

Resolved, FalsePositive and Suppressed are final. Anything else is refused (409).
"""

from typing import Final

from ransomguard_grid.db.models.enums import AlertStatus

ALLOWED_TRANSITIONS: Final[dict[AlertStatus, frozenset[AlertStatus]]] = {
    AlertStatus.New: frozenset({AlertStatus.Investigating}),
    AlertStatus.Investigating: frozenset({AlertStatus.Resolved, AlertStatus.FalsePositive, AlertStatus.Suppressed}),
    AlertStatus.Resolved: frozenset(),
    AlertStatus.FalsePositive: frozenset(),
    AlertStatus.Suppressed: frozenset(),
}


def is_allowed(old: AlertStatus, new: AlertStatus) -> bool:
    return new in ALLOWED_TRANSITIONS.get(old, frozenset())
