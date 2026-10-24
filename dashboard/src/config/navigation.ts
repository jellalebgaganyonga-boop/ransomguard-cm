import type { LucideIcon } from 'lucide-react';
import { LayoutDashboard, ShieldAlert, Monitor, Users, ScrollText, Settings, ClipboardList } from 'lucide-react';
import type { Role } from '@/api/me';

/**
 * L1 Navigation — Definition Phase Day 4 §1.
 *
 * Each item declares which roles can see it. The Sidebar organism
 * filters this list by the current user's role (from useMe()) — items
 * are HIDDEN entirely for unauthorized roles, never shown-disabled
 * (Definition Phase Day 4 §6 "Sidebar L1 States": "Items hidden entirely
 * (NOT shown grayed out)").
 *
 * This is a UX convenience only. RBAC enforcement happens server-side
 * on every API call (STRIDE WT4.2, WT4.5) — hiding a nav item does not
 * by itself secure anything.
 */

export interface NavItem {
  /** Translation key under `nav.*` (src/i18n/locales/{fr,en}.json) */
  labelKey: string;
  /** Route path */
  to: string;
  icon: LucideIcon;
  /** Roles that may open the page (the route guard, protected-route.tsx). */
  roles: Role[];
  /** Roles that see the entry in the sidebar; every role of `roles` when omitted. */
  sidebarRoles?: Role[];
}

export const NAV_ITEMS: NavItem[] = [
  {
    labelKey: 'nav.dashboard',
    to: '/dashboard',
    icon: LayoutDashboard,
    roles: ['tenant_admin', 'security_analyst', 'read_only_auditor'],
  },
  {
    labelKey: 'nav.alerts',
    to: '/alerts',
    icon: ShieldAlert,
    roles: ['tenant_admin', 'security_analyst', 'read_only_auditor'],
  },
  {
    labelKey: 'nav.agents',
    to: '/agents',
    icon: Monitor,
    roles: ['tenant_admin', 'security_analyst', 'read_only_auditor'],
  },
  {
    labelKey: 'nav.users',
    to: '/users',
    icon: Users,
    roles: ['tenant_admin'],
  },
  {
    labelKey: 'nav.audit',
    to: '/audit',
    icon: ScrollText,
    roles: ['tenant_admin', 'read_only_auditor'],
  },
  {
    labelKey: 'nav.userActions',
    to: '/audit/user-actions',
    icon: ClipboardList,
    roles: ['tenant_admin', 'read_only_auditor'],
  },
  {
    // Settings (Fix C): every role edits their own name, language, notifications and password;
    // the sidebar entry is for administrators, the others get there from the header user menu.
    // The former 'Notifications' entry is a tab of this page now.
    labelKey: 'nav.settings',
    to: '/settings',
    icon: Settings,
    roles: ['tenant_admin', 'security_analyst', 'read_only_auditor'],
    sidebarRoles: ['tenant_admin'],
  },
];

export function getNavItemsForRole(role: Role): NavItem[] {
  return NAV_ITEMS.filter((item) => (item.sidebarRoles ?? item.roles).includes(role));
}
