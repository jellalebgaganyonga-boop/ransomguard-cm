/**
 * EPIC-AGENTS — TanStack Query hooks
 *
 * AC4.4.2: agents list auto-refreshes every 30 seconds.
 * Query keys namespaced by tenant_id (STRIDE WT1.6 cross-tenant cache isolation).
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
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

// ── Agent list (US4.1, AC4.4.2: 30s polling) ───────────────

export function useAgentList(params: AgentListParams = {}) {
  const tenantId = useTenantId();
  return useQuery({
    queryKey: ['agents', 'list', tenantId, params],
    queryFn: () => fetchAgentList(params),
    enabled: !!tenantId,
    refetchInterval: 30_000,
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
    staleTime: 30_000,
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
