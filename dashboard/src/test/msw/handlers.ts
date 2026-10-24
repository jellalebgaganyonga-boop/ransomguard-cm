import { http, HttpResponse } from 'msw';

/**
 * MSW handlers — Day 1 bootstrap.
 *
 * Provides a default mock for GET /api/v1/dashboard/me matching the
 * Phase 0 contract (phase0_backend/schemas/me.py), so component tests
 * for AppShellLayout/Sidebar/Header/ProtectedRoute can render without a
 * real backend. Individual tests override via server.use(...) for
 * role-specific or error scenarios (e.g., security_analyst, 401).
 */

export const MOCK_ME_ANALYST = {
  user_id: '550e8400-e29b-41d4-a716-446655440000',
  email: 'marc.tchoumi@hopital-yde.cm',
  full_name: 'Marc Tchoumi',
  is_active: true,
  tenant: {
    id: '650e8400-e29b-41d4-a716-446655440001',
    name: 'Hôpital Régional de Yaoundé',
    status: 'active' as const,
  },
  roles: ['security_analyst'],
  last_login_at: '2026-06-08T05:42:31Z',
  preferences: {
    language: 'fr' as const,
    timezone: 'Africa/Douala',
  },
};

export const handlers = [
  http.get('/api/v1/dashboard/me', () => {
    return HttpResponse.json(MOCK_ME_ANALYST);
  }),

  http.post('/api/v1/auth/refresh', () => {
    return HttpResponse.json({ access_token: 'mock-access-token' });
  }),
];
