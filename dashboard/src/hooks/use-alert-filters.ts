/**
 * AC3.1.2: "the URL query string reflects the filters (shareable links)"
 *
 * Single source of truth for alert list filter state, backed by the URL
 * via useSearchParams. This means:
 *   - Reload preserves filters
 *   - Copy-paste URL shares the exact filtered view
 *   - Browser back/forward navigates filter history
 */

import { useSearchParams } from 'react-router-dom';
import { useCallback, useMemo } from 'react';
import type { AlertListParams, AlertSeverity, AlertStatus } from '@/api/alerts';

const DEFAULT_PAGE_SIZE = 50; // AC3.1.1

export function useAlertFilters() {
  const [searchParams, setSearchParams] = useSearchParams();

  const filters: AlertListParams = useMemo(() => {
    const params: AlertListParams = {
      page: Number(searchParams.get('page')) || 1,
      page_size: Number(searchParams.get('page_size')) || DEFAULT_PAGE_SIZE,
    };

    const dateFrom = searchParams.get('date_from');
    if (dateFrom) params.date_from = dateFrom;

    const dateTo = searchParams.get('date_to');
    if (dateTo) params.date_to = dateTo;

    const severity = searchParams.get('severity');
    if (severity) params.severity = severity as AlertSeverity;

    const status = searchParams.get('status');
    if (status) params.status = status as AlertStatus;

    const agent = searchParams.get('agent');
    if (agent) params.agent = agent;

    const module = searchParams.get('module');
    if (module) params.module = module;

    const sortBy = searchParams.get('sort_by');
    if (sortBy === 'created_at' || sortBy === 'severity' || sortBy === 'status') {
      params.sort_by = sortBy;
    }

    const sortDir = searchParams.get('sort_dir');
    if (sortDir === 'asc' || sortDir === 'desc') {
      params.sort_dir = sortDir;
    }

    return params;
  }, [searchParams]);

  const setFilter = useCallback(
    (key: keyof AlertListParams, value: string | number | undefined) => {
      setSearchParams((prev) => {
        const next = new URLSearchParams(prev);
        if (value === undefined || value === '') {
          next.delete(key);
        } else {
          next.set(key, String(value));
        }
        // AC: any filter change resets to page 1 (avoid landing on an
        // out-of-range page after narrowing results)
        if (key !== 'page') {
          next.delete('page');
        }
        return next;
      });
    },
    [setSearchParams]
  );

  const resetFilters = useCallback(() => {
    setSearchParams(new URLSearchParams());
  }, [setSearchParams]);

  const hasActiveFilters = useMemo(
    () =>
      !!(filters.severity || filters.status || filters.agent || filters.module || filters.date_from || filters.date_to),
    [filters]
  );

  return { filters, setFilter, resetFilters, hasActiveFilters };
}
