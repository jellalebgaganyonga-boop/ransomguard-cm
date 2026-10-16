import { useQuery } from '@tanstack/react-query';
import { fetchUserActionLogs, type UserActionLogParams } from '@/api/user-actions';

export function useUserActions(params: UserActionLogParams = {}) {
  return useQuery({
    queryKey: ['user-actions', params],
    queryFn: () => fetchUserActionLogs(params),
    refetchInterval: 30_000,
  });
}
