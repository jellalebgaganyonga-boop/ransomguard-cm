"""The seed script releases its connection pool before the event loop closes."""

import importlib.util
from pathlib import Path
from types import ModuleType

import pytest

_SCRIPT = Path(__file__).resolve().parents[1] / "scripts" / "seed_initial_data.py"


def _load_seed_module() -> ModuleType:
    spec = importlib.util.spec_from_file_location("seed_initial_data", _SCRIPT)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class _FakeEngine:
    def __init__(self) -> None:
        self.disposed = 0

    async def dispose(self) -> None:
        self.disposed += 1


async def test_main_disposes_the_engine_after_a_successful_seed(monkeypatch: pytest.MonkeyPatch) -> None:
    seed_module = _load_seed_module()
    fake_engine = _FakeEngine()
    calls: list[str] = []

    async def fake_seed() -> None:
        calls.append("seed")

    monkeypatch.setattr(seed_module, "seed", fake_seed)
    monkeypatch.setattr(seed_module, "engine", fake_engine)

    await seed_module.main()

    assert calls == ["seed"]
    assert fake_engine.disposed == 1


async def test_main_disposes_the_engine_when_the_seed_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    seed_module = _load_seed_module()
    fake_engine = _FakeEngine()

    async def refusing_seed() -> None:
        raise SystemExit(1)

    monkeypatch.setattr(seed_module, "seed", refusing_seed)
    monkeypatch.setattr(seed_module, "engine", fake_engine)

    with pytest.raises(SystemExit):
        await seed_module.main()

    assert fake_engine.disposed == 1


async def test_seed_refuses_without_an_admin_email_and_creates_nothing(
    monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    seed_module = _load_seed_module()
    monkeypatch.delenv("GRID_SEED_ADMIN_EMAIL", raising=False)

    def no_database(*_args: object, **_kwargs: object) -> None:
        raise AssertionError("the seed opened the database although it must refuse first")

    monkeypatch.setattr(seed_module, "AsyncSessionLocal", no_database)

    with pytest.raises(SystemExit) as exit_info:
        await seed_module.seed()

    assert exit_info.value.code == 1
    assert "GRID_SEED_ADMIN_EMAIL is required" in capsys.readouterr().err


async def test_seed_refuses_a_blank_admin_email(monkeypatch: pytest.MonkeyPatch) -> None:
    seed_module = _load_seed_module()
    monkeypatch.setenv("GRID_SEED_ADMIN_EMAIL", "   ")
    monkeypatch.setattr(seed_module, "AsyncSessionLocal", lambda *a, **k: (_ for _ in ()).throw(AssertionError()))

    with pytest.raises(SystemExit):
        await seed_module.seed()
