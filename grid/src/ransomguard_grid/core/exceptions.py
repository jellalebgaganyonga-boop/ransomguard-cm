"""Application-level exceptions for GRID server."""


class GridBaseException(Exception):
    """Base exception for all GRID errors."""

    def __init__(self, message: str, status_code: int = 500) -> None:
        self.message = message
        self.status_code = status_code
        super().__init__(message)


class TenantNotFoundError(GridBaseException):
    """Raised when a tenant ID does not exist."""

    def __init__(self, tenant_id: str) -> None:
        super().__init__(f"Tenant not found: {tenant_id}", 404)


class AgentNotFoundError(GridBaseException):
    """Raised when an agent ID does not exist within the tenant."""

    def __init__(self, agent_id: str) -> None:
        super().__init__(f"Agent not found: {agent_id}", 404)


class AuthenticationError(GridBaseException):
    """Raised on authentication failure (mTLS or JWT)."""

    def __init__(self, detail: str = "Authentication required") -> None:
        super().__init__(detail, 401)


class AuthorizationError(GridBaseException):
    """Raised when authenticated user lacks required role."""

    def __init__(self, detail: str = "Insufficient permissions") -> None:
        super().__init__(detail, 403)


class TenantIsolationViolation(GridBaseException):
    """Raised on cross-tenant access attempt (CWE-285)."""

    def __init__(self) -> None:
        super().__init__("Resource not found", 404)  # 404, not 403 (no info leak)


class DuplicateResourceError(GridBaseException):
    """Raised when creating a resource that already exists."""

    def __init__(self, resource: str) -> None:
        super().__init__(f"Resource already exists: {resource}", 409)


class RateLimitExceededError(GridBaseException):
    """Raised when rate limit is exceeded."""

    def __init__(self) -> None:
        super().__init__("Rate limit exceeded", 429)
