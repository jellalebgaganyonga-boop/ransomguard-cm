import * as React from 'react';
import { cn } from '@/lib/utils';

/**
 * Input — Atom #3 (Atomic Design inventory, Design Phase Day 7-8 §1).
 * Styling maps to cmp.input.* tokens (Design Phase Day 6 §4.5).
 */

export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  /** Adds the error border treatment (cmp.input.border.color.error). */
  hasError?: boolean;
}

export const Input = React.forwardRef<HTMLInputElement, InputProps>(
  ({ className, hasError, type = 'text', ...props }, ref) => {
    return (
      <input
        ref={ref}
        type={type}
        className={cn(
          'flex h-10 w-full rounded-md border bg-canvas px-3 py-2 text-sm text-text-primary',
          'placeholder:text-text-tertiary',
          'transition-colors duration-fast ease-standard',
          'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-border-focus focus-visible:ring-offset-2',
          'disabled:cursor-not-allowed disabled:bg-disabled-bg disabled:text-disabled-text',
          hasError
            ? 'border-error focus-visible:ring-error'
            : 'border-border-default hover:border-border-strong',
          className
        )}
        aria-invalid={hasError || undefined}
        {...props}
      />
    );
  }
);
Input.displayName = 'Input';
