"""Structured logging via structlog with sensitive field redaction."""

import structlog

REDACTED_FIELDS = frozenset({
    "password", "secret", "api_key", "apikey", "token",
    "jwt", "private_key", "privatekey", "cert_content",
    "hashed_password", "otp",
})


def redact_sensitive_fields(
    logger: structlog.types.WrappedLogger,
    method_name: str,
    event_dict: structlog.types.EventDict,
) -> structlog.types.EventDict:
    """Processor that replaces sensitive field values with [REDACTED]."""
    for key in list(event_dict.keys()):
        if key.lower() in REDACTED_FIELDS:
            event_dict[key] = "[REDACTED]"
    return event_dict


def configure_logging(log_level: str = "INFO", json_output: bool = False) -> None:
    """Configure structlog with redaction and optional JSON output."""
    processors: list[structlog.types.Processor] = [
        structlog.contextvars.merge_contextvars,
        structlog.stdlib.add_log_level,
        structlog.stdlib.add_logger_name,
        structlog.processors.TimeStamper(fmt="iso"),
        structlog.processors.StackInfoRenderer(),
        redact_sensitive_fields,
    ]

    if json_output:
        processors.append(structlog.processors.JSONRenderer())
    else:
        processors.append(structlog.dev.ConsoleRenderer())

    structlog.configure(
        processors=processors,
        wrapper_class=structlog.stdlib.BoundLogger,
        context_class=dict,
        logger_factory=structlog.PrintLoggerFactory(),
        cache_logger_on_first_use=True,
    )


def get_logger(name: str | None = None) -> structlog.stdlib.BoundLogger:
    """Get a structured logger instance."""
    return structlog.get_logger(name)  # type: ignore[return-value]
