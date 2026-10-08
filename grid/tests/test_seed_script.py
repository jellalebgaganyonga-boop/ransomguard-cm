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
