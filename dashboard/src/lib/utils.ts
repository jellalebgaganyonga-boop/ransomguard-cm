import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

/**
 * Merge Tailwind class names, resolving conflicts (last wins) via
 * tailwind-merge and supporting conditional classes via clsx.
 *
 * Standard shadcn/ui utility — used by every UI primitive.
 */
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}
