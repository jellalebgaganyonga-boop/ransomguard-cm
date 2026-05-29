"""Tests for FastAPI main application."""

import pytest
from httpx import AsyncClient


@pytest.mark.asyncio
async def test_health_endpoint_returns_200(client: AsyncClient) -> None:
    """GET /api/v1/health should return 200 with status ok."""
    response = await client.get("/api/v1/health")
    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "ok"
    assert data["version"] == "0.8.0"


@pytest.mark.asyncio
async def test_health_ready_checks_db(client: AsyncClient) -> None:
    """GET /api/v1/health/ready should check database connectivity."""
    response = await client.get("/api/v1/health/ready")
    assert response.status_code == 200
    data = response.json()
    assert "database" in data
    assert "status" in data


@pytest.mark.asyncio
async def test_unknown_route_returns_404(client: AsyncClient) -> None:
    """Unknown routes should return 404."""
    response = await client.get("/api/v1/nonexistent")
    assert response.status_code == 404


@pytest.mark.asyncio
async def test_request_id_header_propagated(client: AsyncClient) -> None:
    """X-Request-ID should be returned in response headers."""
    response = await client.get("/api/v1/health", headers={"X-Request-ID": "test-123"})
    assert response.headers.get("X-Request-ID") == "test-123"
