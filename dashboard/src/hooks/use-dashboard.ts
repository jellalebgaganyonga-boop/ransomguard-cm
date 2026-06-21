// ============================================================
// src/hooks/use-dashboard.ts
// EPIC-DASHBOARD — TanStack Query hooks
//
// AC2.2.6: executive dashboard polls every 30s
// AC2.3.2: operational feed polls every 15s
// Cache keys namespaced by tenant_id (STRIDE WT1.6)
// ============================================================

import { useQuery } from '@tanstack/react-query';
import { useMe } from './use-me';
import { fetchMetricsSummary } from '@/api/dashboard';
import {
  fetchAlertList,
  type AlertListParams,
} from '@/api/alerts';
import { fetchAgentList } from '@/api/agents';

// ── Shared: get tenant_id from /me for cache namespacing ──

function useTenantId(): string | undefined {
  const { data: me } = useMe();
  return me?.tenant.id;
}

// ── MetricsSummary (AC2.2.1-2, AC2.3.1, AC2.2.6: 30s) ────

export function useMetricsSummary() {
  const tenantId = useTenantId();
  return useQuery({
    queryKey: ['metrics', 'summary', tenantId],
    queryFn: fetchMetricsSummary,
    enabled: !!tenantId,
    refetchInterval: 30_000, // AC2.2.6: 30s auto-refresh
    staleTime: 25_000,
  });
}

// ── Alerts list (AC2.2.3: top-5 for exec, AC2.3.2: 20 for ops) ──
// Uses fetchAlertList from alerts.ts — correct offset/limit pagination
// and correct field names (BUG-02 fix: was using broken dashboard.ts fetchAlerts)

export function useAlerts(params: AlertListParams = {}, pollMs?: number) {
  const tenantId = useTenantId();
  return useQuery({
    queryKey: ['alerts', tenantId, params],
    queryFn: () => fetchAlertList(params),
    enabled: !!tenantId,
    refetchInterval: pollMs ?? false,
    staleTime: pollMs ? pollMs - 5_000 : 30_000,
  });
}

// AC2.2.3: exec dashboard — 5 most recent, 30s polling
export function useRecentAlerts() {
  return useAlerts({ limit: 5 }, 30_000);
}

// AC2.3.2: operational dashboard — 20 alerts, 15s polling
export function useLiveAlerts(params: AlertListParams = {}) {
  return useAlerts({ limit: 20, ...params }, 15_000);
}

// AC2.2.2: last critical alert for "days since" KPI
// (last_critical_at is NOT in MetricsSummary — derive from alert list)
export function useLastCriticalAlert() {
  const tenantId = useTenantId();
  return useQuery({
    queryKey: ['alerts', 'last-critical', tenantId],
    queryFn: () => fetchAlertList({ severity: 'Critical', limit: 1 }),
    enabled: !!tenantId,
    refetchInterval: 30_000,
    staleTime: 25_000,
  });
}

// ── Agents (AC2.3.1: health summary, AC2.3.3: top-10, AC4.4.2: 30s) ──

export function useAgents(pageSize = 10) {
  const tenantId = useTenantId();
  return useQuery({
    queryKey: ['agents', 'list', tenantId, { limit: pageSize }],
    queryFn: () => fetchAgentList({ limit: pageSize }),
    enabled: !!tenantId,
    refetchInterval: 30_000, // AC4.4.2
    staleTime: 25_000,
  });
}

// ── Derived: days since last critical (AC2.2.2) ───────────

export function daysSinceLastCritical(lastCriticalAt: string | null): number | null {
  if (!lastCriticalAt) return null;
  const ms = Date.now() - new Date(lastCriticalAt).getTime();
  return Math.floor(ms / (1000 * 60 * 60 * 24));
}
