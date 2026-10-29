import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders } from 'axios';
import { RoleEditorModal } from './role-editor-modal';

/**
 * The role dialog, after the owner reported on 2026-10-27 that assigning "Security analyst" to
 * kelyfa3@gmail.com showed only "An error occurred" -- over a change the server had saved.
 */

const updateRoles = vi.fn();
const usersWithRoles = vi.fn();

vi.mock('@/hooks/use-users', () => ({
  useUpdateUserRoles: () => ({
    mutateAsync: updateRoles,
    isPending: false,
    error: updateRolesError,
  }),
  useUsersWithRoles: () => usersWithRoles(),
}));

let updateRolesError: unknown = null;

function loaded(roles: string[]) {
  usersWithRoles.mockReturnValue({
    data: { items: [{ id: 'u1', email: 'kelyfa3@gmail.com', full_name: 'K L F', is_active: true, last_login_at: null, roles }], total: 1, offset: 0, limit: 200 },
    isLoading: false, isError: false, error: null,
  });
}

function show() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <RoleEditorModal userId="u1" userEmail="kelyfa3@gmail.com" currentUserId="admin1"
        onClose={() => {}} onSuccess={() => {}} />
    </QueryClientProvider>,
  );
}

afterEach(() => {
  cleanup();
  updateRolesError = null;
  updateRoles.mockReset();
  usersWithRoles.mockReset();
});

describe('RoleEditorModal', () => {
  it('ticks the roles the user already has', async () => {
    loaded(['security_analyst']);
    show();

    await waitFor(() =>
      expect(screen.getByRole('checkbox', { name: /security analyst/i })).toBeChecked());
    // Replacing all roles from a blank dialog is how one gets removed by accident.
    expect(screen.getByRole('checkbox', { name: /auditor/i })).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: /admin/i })).not.toBeChecked();
  });

  it('no longer claims current roles cannot be shown', async () => {
    loaded(['security_analyst']);
    show();

    await waitFor(() => expect(screen.queryByText(/known backend limitation/i)).not.toBeInTheDocument());
  });

  it('shows what the server actually said, not "An error occurred"', async () => {
    loaded(['tenant_admin']);
    updateRolesError = new AxiosError('Request failed', '409', undefined,
      {}, {
        status: 409, statusText: 'Conflict', headers: new AxiosHeaders(),
        config: { headers: new AxiosHeaders() },
        data: { detail: 'Cannot remove the tenant_admin role from the last active administrator' },
      });
    show();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/last active administrator/i);
    expect(alert).not.toHaveTextContent(/^An error occurred$/);
  });

  it('keeps what the administrator ticked when the current roles arrive late', async () => {
    // A refetch that re-ticks a box the admin has just cleared is the dialog arguing back.
    loaded(['security_analyst']);
    show();
    await waitFor(() =>
      expect(screen.getByRole('checkbox', { name: /security analyst/i })).toBeChecked());

    await userEvent.click(screen.getByRole('checkbox', { name: /security analyst/i }));
    await userEvent.click(screen.getByRole('checkbox', { name: /auditor/i }));

    expect(screen.getByRole('checkbox', { name: /security analyst/i })).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: /auditor/i })).toBeChecked();
  });

  it('sends exactly what is ticked', async () => {
    loaded([]);
    updateRoles.mockResolvedValue({});
    show();

    await userEvent.click(screen.getByRole('checkbox', { name: /security analyst/i }));
    await userEvent.click(screen.getByRole('button', { name: /save roles/i }));

    await waitFor(() => expect(updateRoles).toHaveBeenCalledWith({
      userId: 'u1', payload: { role_names: ['security_analyst'] },
    }));
  });
});
