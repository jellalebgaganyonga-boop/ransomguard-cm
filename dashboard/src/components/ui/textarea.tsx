import * as React from 'react';
import { cn } from '@/lib/utils';

/**
 * Textarea — Atom #4 (Atomic Design inventory).
 * Used by: Acknowledge modal (AC3.3.1 note), Close modal (AC3.4.1 resolution notes).
 */

export interface TextareaProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {
  hasError?: boolean;
}

export const Textarea = React.forwardRef<HTMLTextAreaElement, TextareaProps>(
  ({ className, hasError, ...props }, ref) => {
    return (
      <textarea
        ref={ref}
        className={cn(
          'flex min-h-[80px] w-full resize-y rounded-md border bg-canvas px-3 py-2 text-sm text-text-primary',
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
Textarea.displayName = 'Textarea';
