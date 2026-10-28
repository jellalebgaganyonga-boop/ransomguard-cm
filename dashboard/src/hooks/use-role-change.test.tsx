import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';

import { ME_REFRESH_MS } from './use-me';
import { useUpdateUserRoles } from './use-users';

/**
 * A role change has two halves, and only one of them is a security boundary.
 *
 * The server half: `require_roles` reads the roles from the database on every request, so a
 * change takes effect on the user's very next request with the token they already hold. That is
 * tested where it is enforced -- grid/tests/api/test_role_change_takes_effect.py.
 *
 * The console half, here: the menus. They are a convenience over the server's answer, so being
 * briefly wrong is not an escalation -- every action behind them is still refused -- but a
 * console that offers an analyst what the server will refuse is a console people stop believing.
 * So ['me'] is re-read on a schedule a person would not notice, and anything the console itself
 * changes about roles invalidates it at once.
 */

vi.mock('@/api/users', () => ({
  updateUserRoles: vi.fn(async () => ({
    id: 'u1', email: 'analyst@test.local', full_name: 'Bench Analyst',
    is_active: true, last_login_at: null, roles: ['read_only_auditor'],
  })),
  fetchUserList: vi.fn(),
  fetchUsersWithRoles: vi.fn(),
  createUser: vi.fn(),
  disableUser: vi.fn(),
  enableUser: vi.fn(),
}));

vi.mock('@/api/me', () => ({
  fetchMe: vi.fn(async () => ({
    user_id: 'admin', email: 'admin@test.local', full_name: 'Admin', is_active: true,
    tenant: { id: 'tenant-1', name: 'Hopital', status: 'active' },
    roles: ['tenant_admin'], last_login_at: null,
    preferences: { language: 'en', timezone: 'Africa/Douala' },
  })),
  updateMe: vi.fn(),
  updatePreferences: vi.fn(),
  changePassword: vi.fn(),
}));

function wrapper(client: QueryClient) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  };
}

describe('a role change reaching a signed-in console', () => {
  it('re-reads who the user is at least once a minute', () => {
    // Five minutes was the old window, and it is long enough for a demoted analyst to keep being
    // offered actions through a whole demo.
    expect(ME_REFRESH_MS).toBeLessThanOrEqual(60_000);
  });

  it('invalidates the user list, the dialog and ["me"] when roles are changed here', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidated: unknown[] = [];
    vi.spyOn(client, 'invalidateQueries').mockImplementation(async (filters) => {
      invalidated.push(filters?.queryKey);
    });

    const { result } = renderHook(() => useUpdateUserRoles(), { wrapper: wrapper(client) });
    result.current.mutate({ userId: 'u1', payload: { role_names: ['read_only_auditor'] } });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(invalidated).toContainEqual(['me']);
    expect(invalidated.some((key) => Array.isArray(key) && key[0] === 'users')).toBe(true);
    expect(invalidated.some((key) => Array.isArray(key) && key[0] === 'users-with-roles')).toBe(
      true,
    );
  });
});
