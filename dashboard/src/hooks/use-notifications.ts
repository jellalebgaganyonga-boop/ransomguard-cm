import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  fetchNotificationPreferences,
  updateNotificationPreferences,
  type UpdateNotificationPreferencePayload,
} from '@/api/notifications';

export function useNotificationPreferences() {
  return useQuery({
    queryKey: ['notification-preferences'],
    queryFn: fetchNotificationPreferences,
  });
}

export function useUpdateNotificationPreferences() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: UpdateNotificationPreferencePayload) =>
      updateNotificationPreferences(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['notification-preferences'] });
    },
  });
}
