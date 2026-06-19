/**
 * EPIC-ALERTS — TanStack Query hooks
 *
 * Per ADR-FE-010 (Optimistic UI): acknowledge/close mutations apply
 * onMutate optimistic updates, revert onError, refetch onSettled.
 *
 * Per STRIDE WT1.6: all query keys namespaced by tenant_id (from useMe).
 */

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useMe } from './use-me';
import {
  fetchAlertList,
  fetchAlertDetail,
  updateAlertStatus,
  type AlertListParams,
  type AlertDetail,
  type UpdateAlertStatusPayload,
} from '@/api/alerts';

function useTenantId(): string | undefined {
  const { data: me } = useMe();
  return me?.tenant.id;
}

// ── List (AC3.1.1-2) ────────────────────────────────────────

export function useAlertList(params: AlertListParams = {}) {
  const tenantId = useTenantId();
  return useQuery({
    queryKey: ['alerts', tenantId, 'list', params],
    queryFn: () => fetchAlertList(params),
    enabled: !!tenantId,
    staleTime: 15_000,
    placeholderData: (prev) => prev, // avoid layout flash on filter/page change
  });
}

// ── Detail (AC3.2.1-6) ─────────────────────────────────────

export function useAlertDetail(alertId: string | undefined) {
  const tenantId = useTenantId();
  return useQuery({
    queryKey: ['alerts', tenantId, 'detail', alertId],
    queryFn: () => fetchAlertDetail(alertId as string),
    enabled: !!tenantId && !!alertId,
    staleTime: 10_000,
    retry: (failureCount, error) => {
      // AC3.2.6: 404 (cross-tenant or non-existent) — don't retry
      const status = (error as { response?: { status?: number } })?.response?.status;
      if (status === 404) return false;
      return failureCount < 1;
    },
  });
}

// ── Status mutation (AC3.3.1-4, AC3.4.1-3) ─────────────────

interface MutationContext {
  previousDetail: AlertDetail | undefined;
}

export function useUpdateAlertStatus(alertId: string) {
  const tenantId = useTenantId();
  const queryClient = useQueryClient();
  const detailKey = ['alerts', tenantId, 'detail', alertId];

  return useMutation<AlertDetail, Error, UpdateAlertStatusPayload, MutationContext>({
    mutationFn: (payload) => updateAlertStatus(alertId, payload),

    // ADR-FE-010: optimistic update — UI reflects new status immediately
    onMutate: async (payload) => {
      await queryClient.cancelQueries({ queryKey: detailKey });
      const previousDetail = queryClient.getQueryData<AlertDetail>(detailKey);

      if (previousDetail) {
        queryClient.setQueryData<AlertDetail>(detailKey, {
          ...previousDetail,
          status: payload.status,
          status_history: [
            ...previousDetail.status_history,
            {
              from_status: previousDetail.status,
              to_status: payload.status,
              changed_at: new Date().toISOString(),
              actor_user_id: null, // filled by server on refetch
              actor_name: null,
              note: payload.note ?? null,
            },
          ],
        });
      }

      return { previousDetail };
    },

    // AC3.3.4: revert on failure
    onError: (_err, _payload, context) => {
      if (context?.previousDetail) {
        queryClient.setQueryData(detailKey, context.previousDetail);
      }
    },

    // Refetch to confirm server truth (actor_name, server timestamp, audit confirmation)
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: detailKey });
      void queryClient.invalidateQueries({ queryKey: ['alerts', tenantId, 'list'] });
      void queryClient.invalidateQueries({ queryKey: ['metrics', 'summary', tenantId] });
    },
  });
}
