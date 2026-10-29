/**
 * EPIC-AUDIT — TanStack Query hook
 *
 * No auto-refresh — audit logs are immutable append-only records.
 * Query keys namespaced by tenant_id (STRIDE WT1.6 cross-tenant cache isolation).
 */

import { useQuery } from '@tanstack/react-query';
import { fetchAuditLogs, type AuditLogParams } from '@/api/audit';
import { useMe } from './use-me';

function useTenantId(): string | undefined {
  const { data: me } = useMe();
  return me?.tenant.id;
}

export function useAuditLogs(params: AuditLogParams = {}) {
  const tenantId = useTenantId();
  return useQuery({
    queryKey: ['audit-logs', tenantId, params],
    queryFn: () => fetchAuditLogs(params),
    enabled: !!tenantId,
    staleTime: 60_000,
  });
}
