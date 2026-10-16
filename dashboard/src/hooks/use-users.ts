/**
 * EPIC-USERS — TanStack Query hooks
 * Cache keys namespaced by tenant_id (STRIDE WT1.6, same as other hooks).
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useMe } from './use-me';
import {
  fetchUserList,
  createUser,
  disableUser,
  enableUser,
  updateUserRoles,
  type UserListParams,
  type CreateUserPayload,
  type UpdateRolesPayload,
} from '@/api/users';

function useTenantId(): string | undefined {
  const { data: me } = useMe();
  return me?.tenant.id;
}

// ── User list ──────────────────────────────────────────────

export function useUserList(params: UserListParams = {}) {
  const tenantId = useTenantId();
  return useQuery({
    queryKey: ['users', tenantId, params],
    queryFn: () => fetchUserList(params),
    enabled: !!tenantId,
    staleTime: 30_000,
  });
}

// ── Mutations ──────────────────────────────────────────────

export function useCreateUser() {
  const queryClient = useQueryClient();
  const tenantId = useTenantId();
  return useMutation({
    mutationFn: (payload: CreateUserPayload) => createUser(payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['users', tenantId] });
    },
  });
}

export function useDisableUser() {
  const queryClient = useQueryClient();
  const tenantId = useTenantId();
  return useMutation({
    mutationFn: (userId: string) => disableUser(userId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['users', tenantId] });
    },
  });
}

export function useEnableUser() {
  const queryClient = useQueryClient();
  const tenantId = useTenantId();
  return useMutation({
    mutationFn: (userId: string) => enableUser(userId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['users', tenantId] });
    },
  });
}

export function useUpdateUserRoles() {
  const queryClient = useQueryClient();
  const tenantId = useTenantId();
  return useMutation({
    mutationFn: ({ userId, payload }: { userId: string; payload: UpdateRolesPayload }) =>
      updateUserRoles(userId, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['users', tenantId] });
    },
  });
}
