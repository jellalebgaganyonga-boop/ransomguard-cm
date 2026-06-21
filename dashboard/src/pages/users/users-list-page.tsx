// ============================================================
// src/pages/users/users-list-page.tsx
// EPIC-USERS — US5.1-5.4 (tenant_admin only)
//
// US5.1: List users with email / full_name / status / last_login
// US5.2: Create user → CreateUserModal
// US5.3: Enable/Disable → DisableEnableModal (GAP-05: self-disable guard)
// US5.4: Edit roles → RoleEditorModal (GAP-08: checkboxes start unchecked)
//
// GAP-08: roles column ABSENT from list — backend GET /users does not
//   return roles in UserItem. "Modifier rôles" opens RoleEditorModal
//   which starts with all checkboxes unchecked and shows a warning.
// ============================================================

import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { UserPlus, Shield } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useMe } from '@/hooks/use-me';
import { useUserList } from '@/hooks/use-users';
import { canDisable, canEnable } from '@/lib/user-permissions';
import { formatDistanceToNow } from '@/lib/format-date';
import { DisableEnableModal } from '@/components/users/disable-enable-modal';
import { RoleEditorModal } from '@/components/users/role-editor-modal';
import { CreateUserModal } from '@/components/users/create-user-modal';
import type { UserItem } from '@/api/users';

// ── Pagination constants ───────────────────────────────────
const PAGE_SIZE = 20;

// ── Modal state ────────────────────────────────────────────
type ModalState =
  | { kind: 'none' }
  | { kind: 'create' }
  | { kind: 'disable' | 'enable'; user: UserItem }
  | { kind: 'roles'; user: UserItem };

// ── Page ──────────────────────────────────────────────────

export function UsersListPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  // All hooks must be called unconditionally (React rules of hooks)
  const { data: me } = useMe();
  const currentUserId = me?.user_id ?? '';

  const [offset, setOffset] = useState(0);
  const [modal, setModal] = useState<ModalState>({ kind: 'none' });

  const { data, isLoading, isError, refetch } = useUserList({ offset, limit: PAGE_SIZE });

  // Redirect non-admin (belt-and-suspenders — ProtectedRoute already guards /users)
  if (me && !me.roles.includes('tenant_admin')) {
    navigate('/dashboard', { replace: true });
    return null;
  }
  const users = data?.items ?? [];
  const total = data?.total ?? 0;
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));
  const currentPage = Math.floor(offset / PAGE_SIZE) + 1;

  function closeModal() {
    setModal({ kind: 'none' });
  }

  return (
    <div>
      <div className="mb-6 flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-text-primary">
            {t('nav.users', 'Utilisateurs')}
          </h1>
          <p className="mt-1 text-sm text-text-secondary">
            {total > 0
              ? `${total} ${t('users.list.totalUsers', 'utilisateurs enregistrés')}`
              : ''}
          </p>
        </div>
        <Button onClick={() => setModal({ kind: 'create' })}>
          <UserPlus className="size-4" aria-hidden="true" />
          {t('users.createUser', 'Créer un utilisateur')}
        </Button>
      </div>

      {/* Table */}
      <div className="rounded-lg border border-border-subtle bg-elevated shadow-sm">
        {isLoading && (
          <p className="py-10 text-center text-sm text-text-tertiary">
            {t('common.loading', 'Chargement...')}
          </p>
        )}

        {isError && (
          <div className="py-10 text-center">
            <p className="mb-3 text-sm text-severity-critical">{t('common.error')}</p>
            <Button variant="ghost" size="sm" onClick={() => void refetch()}>
              {t('common.retry', 'Réessayer')}
            </Button>
          </div>
        )}

        {!isLoading && !isError && users.length === 0 && (
          <p className="py-10 text-center text-sm text-text-tertiary">
            {t('users.list.empty', 'Aucun utilisateur trouvé')}
          </p>
        )}

        {!isLoading && !isError && users.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full border-collapse text-sm">
              <thead>
                <tr className="border-b border-border-default">
                  {[
                    t('users.fullName', 'Nom complet'),
                    t('users.email', 'Email'),
                    t('users.status', 'Statut'),
                    t('users.lastLogin', 'Dernière connexion'),
                    t('alerts.list.col.actions', 'Actions'),
                  ].map((h) => (
                    <th
                      key={h}
                      className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-text-secondary"
                    >
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {users.map((user) => (
                  <tr
                    key={user.id}
                    className="border-b border-border-subtle last:border-0 hover:bg-hover-bg"
                  >
                    <td className="px-4 py-3 font-semibold text-text-primary">
                      {user.full_name}
                      {user.id === currentUserId && (
                        <span className="ml-2 rounded-full bg-primary-subtle px-2 py-0.5 text-xs text-primary">
                          {t('users.list.you', 'vous')}
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3 font-mono text-text-secondary">
                      {user.email}
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                          user.is_active
                            ? 'bg-success-subtle text-success'
                            : 'bg-severity-high-subtle text-severity-high'
                        }`}
                      >
                        {user.is_active
                          ? t('users.active', 'Actif')
                          : t('users.disabled', 'Désactivé')}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-text-tertiary">
                      {user.last_login_at
                        ? formatDistanceToNow(user.last_login_at)
                        : t('users.never', 'Jamais')}
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-2">
                        {/* GAP-08: roles column absent, role editor is an action instead */}
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setModal({ kind: 'roles', user })}
                          title={t('users.editRoles', 'Modifier les rôles')}
                        >
                          <Shield className="size-3.5" aria-hidden="true" />
                          {t('users.editRoles', 'Rôles')}
                        </Button>

                        {user.is_active && canDisable(user.id, currentUserId) && (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => setModal({ kind: 'disable', user })}
                          >
                            {t('users.disable', 'Désactiver')}
                          </Button>
                        )}

                        {!user.is_active && canEnable(user.id, currentUserId) && (
                          <Button
                            variant="secondary"
                            size="sm"
                            onClick={() => setModal({ kind: 'enable', user })}
                          >
                            {t('users.enable', 'Réactiver')}
                          </Button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Pagination */}
      {total > PAGE_SIZE && (
        <div className="mt-4 flex items-center justify-between">
          <Button
            variant="ghost"
            size="sm"
            disabled={offset === 0}
            onClick={() => setOffset((o) => Math.max(0, o - PAGE_SIZE))}
          >
            {t('common.previous', 'Précédent')}
          </Button>
          <span className="text-xs text-text-tertiary">
            {t('common.page', 'Page {page} / {totalPages}')
              .replace('{page}', String(currentPage))
              .replace('{totalPages}', String(totalPages))}
          </span>
          <Button
            variant="ghost"
            size="sm"
            disabled={offset + PAGE_SIZE >= total}
            onClick={() => setOffset((o) => o + PAGE_SIZE)}
          >
            {t('common.next', 'Suivant')}
          </Button>
        </div>
      )}

      {/* Modals */}
      {modal.kind === 'create' && (
        <CreateUserModal
          onClose={closeModal}
          onSuccess={() => void refetch()}
        />
      )}
      {(modal.kind === 'disable' || modal.kind === 'enable') && (
        <DisableEnableModal
          mode={modal.kind}
          userId={modal.user.id}
          userEmail={modal.user.email}
          currentUserId={currentUserId}
          onClose={closeModal}
          onSuccess={() => void refetch()}
        />
      )}
      {modal.kind === 'roles' && (
        <RoleEditorModal
          userId={modal.user.id}
          userEmail={modal.user.email}
          currentUserId={currentUserId}
          onClose={closeModal}
          onSuccess={() => void refetch()}
        />
      )}
    </div>
  );
}
