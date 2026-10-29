"""Email notification service using SMTP relay (Mailjet or any SMTP provider).

Sends alert notifications to tenant admins when critical/high severity alerts
are ingested. Gracefully degrades when SMTP is not configured.
"""

import asyncio
from datetime import datetime
from email.mime.multipart import MIMEMultipart
from email.mime.text import MIMEText
from smtplib import SMTP, SMTPException

from ransomguard_grid.core.config import get_settings
from ransomguard_grid.core.logging import get_logger

logger = get_logger("notification")


class NotificationService:
    """Send email notifications via SMTP relay."""

    def __init__(self) -> None:
        self._settings = get_settings()

    @property
    def is_enabled(self) -> bool:
        return (
            self._settings.smtp_enabled
            and self._settings.smtp_username is not None
            and self._settings.smtp_password is not None
        )

    def _build_alert_email(
        self,
        *,
        to_email: str,
        to_name: str,
        alert_type: str,
        severity: str,
        summary: str,
        agent_hostname: str,
        detected_at: datetime,
        alert_id: str,
        tenant_name: str,
    ) -> MIMEMultipart:
        """Build HTML email for a critical/high alert notification."""
        severity_colors = {
            "Critical": "#DC2626",
            "High": "#EA580C",
            "Medium": "#CA8A04",
            "Low": "#2563EB",
        }
        color = severity_colors.get(severity, "#6B7280")

        html = f"""\
<html>
<body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 0; padding: 20px; background-color: #f3f4f6;">
  <div style="max-width: 600px; margin: 0 auto; background: white; border-radius: 8px; overflow: hidden; box-shadow: 0 1px 3px rgba(0,0,0,0.1);">
    <div style="background: #1e293b; padding: 20px; text-align: center;">
      <h1 style="color: white; margin: 0; font-size: 20px;">RansomGuard-CM</h1>
      <p style="color: #94a3b8; margin: 4px 0 0; font-size: 13px;">{tenant_name}</p>
    </div>
    <div style="padding: 24px;">
      <div style="display: inline-block; background: {color}; color: white; padding: 4px 12px; border-radius: 4px; font-weight: 600; font-size: 14px; margin-bottom: 16px;">
        {severity}
      </div>
      <h2 style="margin: 0 0 8px; color: #1e293b; font-size: 18px;">{alert_type}</h2>
      <p style="color: #475569; margin: 0 0 16px; line-height: 1.5;">{summary}</p>
      <table style="width: 100%; border-collapse: collapse; font-size: 14px;">
        <tr><td style="padding: 8px 0; color: #64748b; width: 140px;">Agent</td><td style="color: #1e293b; font-weight: 500;">{agent_hostname}</td></tr>
        <tr><td style="padding: 8px 0; color: #64748b;">Detected</td><td style="color: #1e293b;">{detected_at.strftime('%Y-%m-%d %H:%M:%S UTC')}</td></tr>
        <tr><td style="padding: 8px 0; color: #64748b;">Alert ID</td><td style="color: #1e293b; font-family: monospace; font-size: 12px;">{alert_id}</td></tr>
      </table>
    </div>
    <div style="background: #f8fafc; padding: 16px 24px; text-align: center; font-size: 12px; color: #94a3b8;">
      This is an automated notification from RansomGuard-CM GRID server.
    </div>
  </div>
</body>
</html>"""

        msg = MIMEMultipart("alternative")
        msg["Subject"] = f"[{severity}] {alert_type} — {agent_hostname}"
        msg["From"] = f"{self._settings.smtp_from_name} <{self._settings.smtp_from_email}>"
        msg["To"] = f"{to_name} <{to_email}>"

        # Plain text fallback
        plain = (
            f"[{severity}] {alert_type}\n\n"
            f"Summary: {summary}\n"
            f"Agent: {agent_hostname}\n"
            f"Detected: {detected_at.strftime('%Y-%m-%d %H:%M:%S UTC')}\n"
            f"Alert ID: {alert_id}\n\n"
            f"— RansomGuard-CM ({tenant_name})"
        )
        msg.attach(MIMEText(plain, "plain"))
        msg.attach(MIMEText(html, "html"))
        return msg

    def _send_smtp(self, msg: MIMEMultipart) -> None:
        """Send email via SMTP (blocking — run in executor)."""
        settings = self._settings
        try:
            with SMTP(settings.smtp_host, settings.smtp_port, timeout=10) as smtp:
                smtp.ehlo()
                smtp.starttls()
                smtp.ehlo()
                smtp.login(settings.smtp_username, settings.smtp_password)  # type: ignore[arg-type]
                smtp.send_message(msg)
            logger.info("Email sent", to=msg["To"], subject=msg["Subject"])
        except SMTPException as exc:
            logger.error("SMTP send failed", error=str(exc), to=msg["To"])
        except Exception as exc:
            logger.error("Email send error", error=str(exc))

    async def notify_alert(
        self,
        *,
        to_email: str,
        to_name: str,
        alert_type: str,
        severity: str,
        summary: str,
        agent_hostname: str,
        detected_at: datetime,
        alert_id: str,
        tenant_name: str,
    ) -> None:
        """Send alert notification email asynchronously."""
        if not self.is_enabled:
            logger.debug("SMTP not configured, skipping notification")
            return

        msg = self._build_alert_email(
            to_email=to_email,
            to_name=to_name,
            alert_type=alert_type,
            severity=severity,
            summary=summary,
            agent_hostname=agent_hostname,
            detected_at=detected_at,
            alert_id=alert_id,
            tenant_name=tenant_name,
        )
        # Run blocking SMTP in thread pool to not block event loop
        loop = asyncio.get_event_loop()
        await loop.run_in_executor(None, self._send_smtp, msg)
