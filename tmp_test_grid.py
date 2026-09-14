"""Test GRID API: login + alert status update + RBAC verification."""
import urllib.request, json, sys

BASE = "http://127.0.0.1:8000/api/v1"

def api(path, token=None, data=None, method=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    body = json.dumps(data).encode() if data else None
    req = urllib.request.Request(f"{BASE}{path}", data=body, headers=headers, method=method)
    try:
        resp = urllib.request.urlopen(req)
        return json.loads(resp.read())
    except urllib.request.HTTPError as e:
        err = e.read().decode()
        return {"_error": e.code, "_detail": err}

# --- 1. Login as admin ---
print("=== LOGIN admin@chu-test.local ===")
tokens = api("/auth/login", data={
    "email": "admin@chu-test.local",
    "password": "ChangeMe123!",
    "tenant_code": "chu-test"
})
if "_error" in tokens:
    print(f"LOGIN FAILED: {tokens}")
    sys.exit(1)
token = tokens["access_token"]
print("Login OK")

# --- 2. Get /me ---
me = api("/dashboard/me", token=token)
print(f"/me: {me.get('email', me)}")

# --- 3. Get alerts ---
alerts = api("/dashboard/alerts?limit=5", token=token)
print(f"Alerts: {alerts.get('total', 'N/A')} total, {len(alerts.get('items', []))} returned")

if alerts.get("items"):
    a = alerts["items"][0]
    aid, status = a.get("alert_id", a.get("id")), a["status"]
    print(f"First alert: {aid} status={status}")

    # Try status update
    new_status = "Investigating" if status == "New" else "Resolved"
    result = api(f"/dashboard/alerts/{aid}/status", token=token,
                 data={"new_status": new_status, "justification": "Testing after Sprint 8 migration fix"},
                 method="POST")
    if "_error" in result:
        print(f"STATUS UPDATE FAILED: {result}")
    else:
        print(f"Status update OK -> {result.get('status', result)}")
else:
    print("No alerts in DB yet")

# --- 4. Get agents ---
agents = api("/dashboard/agents?limit=5", token=token)
print(f"Agents: {agents.get('total', 'N/A')} total")

# --- 5. Provision OTP ---
print("\n=== PROVISION OTP (admin only) ===")
otp_result = api("/dashboard/agents/provision", token=token, data={}, method="POST")
if "_error" in otp_result:
    print(f"PROVISION FAILED: {otp_result}")
else:
    print(f"OTP generated: {otp_result.get('otp', 'N/A')} (expires in {otp_result.get('expires_in_minutes')}min)")

# --- 6. RBAC: analyst login ---
print("\n=== LOGIN analyst@chu-test.local ===")
analyst_tokens = api("/auth/login", data={
    "email": "analyst@chu-test.local",
    "password": "ChangeMe123!",
    "tenant_code": "chu-test"
})
if "_error" in analyst_tokens:
    print(f"Analyst login failed: {analyst_tokens}")
else:
    at = analyst_tokens["access_token"]
    print("Analyst login OK")

    # Analyst should NOT be able to provision
    otp2 = api("/dashboard/agents/provision", token=at, data={}, method="POST")
    expected_403 = "_error" in otp2 and otp2["_error"] == 403
    print(f"Analyst provision blocked (403): {expected_403} {'PASS' if expected_403 else 'FAIL'}")

    # Analyst CAN read alerts
    analyst_alerts = api("/dashboard/alerts?limit=1", token=at)
    can_read = "_error" not in analyst_alerts
    print(f"Analyst read alerts: {can_read} {'PASS' if can_read else 'FAIL'}")

# --- 7. RBAC: auditor login ---
print("\n=== LOGIN auditor@chu-test.local ===")
auditor_tokens = api("/auth/login", data={
    "email": "auditor@chu-test.local",
    "password": "ChangeMe123!",
    "tenant_code": "chu-test"
})
if "_error" in auditor_tokens:
    print(f"Auditor login failed: {auditor_tokens}")
else:
    aut = auditor_tokens["access_token"]
    print("Auditor login OK")

    # Auditor CAN read alerts
    aud_alerts = api("/dashboard/alerts?limit=1", token=aut)
    can_read = "_error" not in aud_alerts
    print(f"Auditor read alerts: {can_read} {'PASS' if can_read else 'FAIL'}")

    # Auditor should NOT be able to update alert status
    if alerts.get("items"):
        aid = alerts["items"][0].get("alert_id", alerts["items"][0].get("id"))
        aud_update = api(f"/dashboard/alerts/{aid}/status", token=aut,
                         data={"new_status": "Investigating", "justification": "Auditor should not do this"},
                         method="POST")
        blocked = "_error" in aud_update and aud_update["_error"] == 403
        print(f"Auditor status update blocked (403): {blocked} {'PASS' if blocked else 'FAIL'}")

print("\n=== ALL TESTS COMPLETE ===")
