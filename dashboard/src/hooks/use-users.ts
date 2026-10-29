/**
 * EPIC-USERS — TanStack Query hooks
 * Cache keys namespaced by tenant_id (STRIDE WT1.6, same as other hooks).
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useMe } from './use-me';
import {
  fetchUserList,
  fetchUsersWithRoles,
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

/**
 * A role change takes effect on the server immediately -- the roles are read from the database on
 * every request, not from the token. What has to catch up is this console: the user list, the
 * dialog's pre-ticked boxes, and ['me'], which decides which menus are drawn and so matters when
 * an administrator changes their own roles or their own browser is the one signed in as the user
 * being changed.
 */
export function useUpdateUserRoles() {
  const queryClient = useQueryClient();
  const tenantId = useTenantId();
  return useMutation({
    mutationFn: ({ userId, payload }: { userId: string; payload: UpdateRolesPayload }) =>
      updateUserRoles(userId, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['users', tenantId] });
      void queryClient.invalidateQueries({ queryKey: ['users-with-roles', tenantId] });
      void queryClient.invalidateQueries({ queryKey: ['me'] });
    },
  });
}

/**
 * The roles each user currently has, for the role dialog to pre-tick.
 */
export function useUsersWithRoles(enabled = true) {
  const tenantId = useTenantId();
  return useQuery({
    queryKey: ['users-with-roles', tenantId],
    queryFn: () => fetchUsersWithRoles(),
    enabled,
  });
}
