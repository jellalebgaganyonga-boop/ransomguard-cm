"""Settings (Fix C): one's own name, language and password, on the GRID.

PATCH /api/v1/dashboard/me, PATCH /api/v1/dashboard/me/preferences, POST /api/v1/dashboard/me/change-password.
"""

import pytest
from httpx import AsyncClient
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models.notifications import UserActionLog
from tests.api.test_tenant_isolation import _PASSWORD, _create_two_tenants, _login

ME = "/api/v1/dashboard/me"


async def _admin(client: AsyncClient) -> tuple[dict[str, str], dict[str, str]]:
    setup = await _create_two_tenants()
    token = await _login(client, setup["tenant_a_code"], setup["user_a_email"])
    return {"Authorization": f"Bearer {token}"}, setup


@pytest.mark.asyncio
async def test_the_display_name_is_changed_returned_and_journalised(client: AsyncClient, db_session: AsyncSession) -> None:
    headers, setup = await _admin(client)

    response = await client.patch(ME, headers=headers, json={"full_name": "  Dr Aïcha Ngo  "})

    assert response.status_code == 200, response.text
    assert response.json()["full_name"] == "Dr Aïcha Ngo"
    assert (await client.get(ME, headers=headers)).json()["full_name"] == "Dr Aïcha Ngo"
    db_session.expire_all()
    log = (await db_session.execute(select(UserActionLog).where(
        UserActionLog.action_type == "profile_updated", UserActionLog.tenant_id == setup["tenant_a_id"],
    ))).scalars().one()
    assert log.details_json["full_name"] == "Dr Aïcha Ngo"


@pytest.mark.asyncio
async def test_a_blank_name_or_an_unknown_field_is_refused(client: AsyncClient) -> None:
    headers, _ = await _admin(client)

    assert (await client.patch(ME, headers=headers, json={"full_name": "   "})).status_code == 422
    assert (await client.patch(ME, headers=headers, json={"full_name": "X", "email": "new@t.local"})).status_code == 422


@pytest.mark.asyncio
async def test_the_language_is_unset_until_chosen_then_kept_on_the_grid(client: AsyncClient) -> None:
    headers, _ = await _admin(client)
    assert (await client.get(ME, headers=headers)).json()["preferences"]["language"] is None  # never chosen

    response = await client.patch(f"{ME}/preferences", headers=headers, json={"language": "fr"})

    assert response.status_code == 200, response.text
    assert response.json() == {"language": "fr", "timezone": "Africa/Douala"}
    assert (await client.get(ME, headers=headers)).json()["preferences"]["language"] == "fr"
    assert (await client.patch(f"{ME}/preferences", headers=headers, json={"language": "de"})).status_code == 422


@pytest.mark.asyncio
async def test_the_password_changes_only_with_the_current_one_and_signs_in_afterwards(
    client: AsyncClient, db_session: AsyncSession,
) -> None:
    headers, setup = await _admin(client)
    url = f"{ME}/change-password"

    assert (await client.post(url, headers=headers, json={"current_password": "not-it-at-all", "new_password": "Nouveau-Mdp-2026"})).status_code == 403
    assert (await client.post(url, headers=headers, json={"current_password": _PASSWORD, "new_password": "short"})).status_code == 422
    assert (await client.post(url, headers=headers, json={"current_password": _PASSWORD, "new_password": _PASSWORD})).status_code == 422

    assert (await client.post(url, headers=headers, json={"current_password": _PASSWORD, "new_password": "Nouveau-Mdp-2026"})).status_code == 204

    old = await client.post("/api/v1/auth/login", json={"tenant_code": setup["tenant_a_code"], "email": setup["user_a_email"], "password": _PASSWORD})
    new = await client.post("/api/v1/auth/login", json={"tenant_code": setup["tenant_a_code"], "email": setup["user_a_email"], "password": "Nouveau-Mdp-2026"})
    assert old.status_code == 401
    assert new.status_code == 200

    db_session.expire_all()
    actions = sorted(a for (a,) in (await db_session.execute(select(UserActionLog.action_type).where(
        UserActionLog.tenant_id == setup["tenant_a_id"], UserActionLog.action_type.like("password_%"),
    ))).all())
    assert actions == ["password_change_refused", "password_changed"]


@pytest.mark.asyncio
async def test_settings_need_a_signed_in_user(client: AsyncClient) -> None:
    assert (await client.patch(ME, json={"full_name": "X"})).status_code == 401
    assert (await client.patch(f"{ME}/preferences", json={"language": "fr"})).status_code == 401
    assert (await client.post(f"{ME}/change-password", json={"current_password": "a", "new_password": "bbbbbbbb"})).status_code == 401
