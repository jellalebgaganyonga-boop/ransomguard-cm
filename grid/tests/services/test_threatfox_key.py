"""ThreatFox needs an abuse.ch Auth-Key (LOT 2 demo: 401 on every run, no key configured).

Without a key the source is not built: no call, one Information log at startup. With a key the API
refuses: no entries, and a Warning at most once an hour.
"""

from typing import Any

import httpx
import pytest

from ransomguard_grid.core.config import Settings
from ransomguard_grid.services.threat_intel_aggregator import ThreatIntelAggregator
from ransomguard_grid.services.threat_intel_sources import catalog, threatfox
from ransomguard_grid.services.threat_intel_sources.catalog import build_sources
from ransomguard_grid.services.threat_intel_sources.threatfox import ThreatFoxSource

THREATFOX_HOST = "threatfox-api.abuse.ch"


class RecordingLogger:
    """Stands in for a module's structlog logger: records (level, event, fields)."""

    def __init__(self) -> None:
        self.records: list[tuple[str, str, dict[str, Any]]] = []

    def info(self, event: str, **fields: Any) -> None:
        self.records.append(("info", event, fields))

    def warning(self, event: str, **fields: Any) -> None:
        self.records.append(("warning", event, fields))

    def at(self, level: str) -> list[tuple[str, str, dict[str, Any]]]:
        return [r for r in self.records if r[0] == level]


class FakeClock:
    def __init__(self) -> None:
        self.now = 1_000.0

    def __call__(self) -> float:
        return self.now


def _settings(threatfox_api_key: str | None) -> Settings:
    return Settings(  # type: ignore[call-arg]
        _env_file=None,
        jwt_secret_key="a-very-long-secret-key-with-32-or-more-chars!",
        threatfox_api_key=threatfox_api_key,
    )


@pytest.mark.parametrize("key", [None, ""])
@pytest.mark.asyncio
async def test_without_a_key_threatfox_is_disabled_one_information_log_and_no_call(
    monkeypatch: pytest.MonkeyPatch, key: str | None,
) -> None:
    log = RecordingLogger()
    monkeypatch.setattr(catalog, "logger", log)
    hosts: list[str] = []

    def handler(request: httpx.Request) -> httpx.Response:
        hosts.append(request.url.host)
        return httpx.Response(200, text="")  # parse errors are swallowed per source

    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as client:
        sources = build_sources(_settings(key), client)
        await ThreatIntelAggregator(sources).fetch_all()
        await ThreatIntelAggregator(sources).fetch_all()

    assert not any(isinstance(s, ThreatFoxSource) for s in sources)
    assert THREATFOX_HOST not in hosts
    assert hosts  # the other sources did run
    infos = log.at("info")
    assert len(infos) == 1
    assert infos[0][2] == {"source": "threatfox"}
    assert "GRID_THREATFOX_API_KEY" in infos[0][1]
    assert log.at("warning") == []


@pytest.mark.asyncio
async def test_with_a_key_threatfox_is_built_and_sends_it_as_auth_key(monkeypatch: pytest.MonkeyPatch) -> None:
    log = RecordingLogger()
    monkeypatch.setattr(catalog, "logger", log)
    seen: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        return httpx.Response(200, json={"query_status": "ok", "data": [
            {"ioc_value": "evil.example", "ioc_type": "domain", "confidence_level": 80},
        ]})

    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as client:
        sources = build_sources(_settings("tf-key-0123"), client)
        threatfox_source = next(s for s in sources if isinstance(s, ThreatFoxSource))
        entries = await threatfox_source.fetch()

    assert [e.indicator for e in entries] == ["evil.example"]
    assert seen[0].url.host == THREATFOX_HOST
    assert seen[0].headers["Auth-Key"] == "tf-key-0123"
    assert log.at("info") == []


@pytest.mark.parametrize("status", [401, 403])
@pytest.mark.asyncio
async def test_a_refused_key_gives_no_entries_and_one_warning_an_hour_at_most(
    monkeypatch: pytest.MonkeyPatch, status: int,
) -> None:
    log = RecordingLogger()
    monkeypatch.setattr(threatfox, "logger", log)
    clock = FakeClock()
    transport = httpx.MockTransport(lambda req: httpx.Response(status, json={"query_status": "unauthorized"}))

    async with httpx.AsyncClient(transport=transport) as client:
        source = ThreatFoxSource(client, "refused-key", clock=clock)
        for _ in range(5):  # five runs within the hour
            assert await source.fetch() == []
            clock.now += 600
        assert len(log.at("warning")) == 1

        clock.now += 3600  # an hour after the first warning, still refused
        assert await source.fetch() == []

    warnings = log.at("warning")
    assert len(warnings) == 2
    assert warnings[0][2] == {"source": "threatfox", "status_code": status}
    assert "refused-key" not in str(warnings)  # the key is never logged


@pytest.mark.asyncio
async def test_other_http_errors_still_raise_for_the_aggregator() -> None:
    transport = httpx.MockTransport(lambda req: httpx.Response(503))
    async with httpx.AsyncClient(transport=transport) as client:
        with pytest.raises(httpx.HTTPStatusError):
            await ThreatFoxSource(client, "k").fetch()
