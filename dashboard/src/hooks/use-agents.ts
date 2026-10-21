/**
 * EPIC-AGENTS — TanStack Query hooks
 *
 * AC4.4.2: agents list auto-refreshes every 30 seconds.
 * Query keys namespaced by tenant_id (STRIDE WT1.6 cross-tenant cache isolation).
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { saveBlob } from '@/lib/installer-download';
import {
  downloadInstaller,
  fetchAgentDetail,
  fetchAgentList,
  issueCommand,
  provisionAgent,
  type AgentListParams,
  type IssueCommandPayload,
} from '@/api/agents';
import { useMe } from './use-me';

function useTenantId(): string | undefined {
  const { data: me } = useMe();
  return me?.tenant.id;
}

/**
 * Every 30 s, and only while the tab is visible: in SaaS every console left open and forgotten
 * would otherwise keep hitting the server (LOT2_DECISIONS_ET_CORRECTIFS.md, point 9).
 */
export const AGENT_REFRESH = {
  refetchInterval: 30_000,
  refetchIntervalInBackground: false,
} as const;

// ── Agent list (US4.1, AC4.4.2: 30s polling) ───────────────

export function useAgentList(params: AgentListParams = {}) {
  const tenantId = useTenantId();
  return useQuery({
    queryKey: ['agents', 'list', tenantId, params],
    queryFn: () => fetchAgentList(params),
    enabled: !!tenantId,
    ...AGENT_REFRESH,
    staleTime: 25_000,
  });
}

// ── Agent detail (US4.2) ────────────────────────────────────

export function useAgentDetail(id: string | undefined) {
  const tenantId = useTenantId();
  return useQuery({
    queryKey: ['agents', 'detail', tenantId, id],
    queryFn: () => fetchAgentDetail(id!),
    enabled: !!tenantId && !!id,
    ...AGENT_REFRESH,
    staleTime: 25_000,
    retry: (count, err) => {
      const status = (err as { response?: { status?: number } })?.response?.status;
      if (status === 404) return false;
      return count < 2;
    },
  });
}

// ── Provision new agent (tenant_admin only) ─────────────────

export function useProvisionAgent() {
  return useMutation({
    mutationFn: () => provisionAgent(),
  });
}

// ── Download the installer (ADR-027, tenant_admin only) ─────

export function useDownloadInstaller() {
  return useMutation({
    mutationFn: (body: { otp: string; host: string; port: number }) => downloadInstaller(body),
    onSuccess: ({ blob, filename }) => saveBlob(blob, filename),
  });
}

// ── Issue command (US4.3, tenant_admin only) ────────────────

export function useIssueCommand() {
  const queryClient = useQueryClient();
  const tenantId = useTenantId();
  return useMutation({
    mutationFn: (payload: IssueCommandPayload) => issueCommand(payload),
    onSuccess: (_data, variables) => {
      // Invalidate the agent detail so status/last command reflects the new queue entry
      void queryClient.invalidateQueries({
        queryKey: ['agents', 'detail', tenantId, variables.agent_id],
      });
    },
  });
}
