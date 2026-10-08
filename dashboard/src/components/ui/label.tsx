import * as React from 'react';
import * as LabelPrimitive from '@radix-ui/react-label';
import { cn } from '@/lib/utils';

/**
 * Label — Atom #5 (Atomic Design inventory, Design Phase Day 7-8 §1).
 * Styling maps to sys.typography.label (Design Phase Day 6 §3.10).
 */

export const Label = React.forwardRef<
  React.ElementRef<typeof LabelPrimitive.Root>,
  React.ComponentPropsWithoutRef<typeof LabelPrimitive.Root>
>(({ className, ...props }, ref) => (
  <LabelPrimitive.Root
    ref={ref}
    className={cn(
      'text-sm font-medium leading-normal tracking-wide text-text-secondary',
      'peer-disabled:cursor-not-allowed peer-disabled:opacity-70',
      className
    )}
    {...props}
  />
));
Label.displayName = LabelPrimitive.Root.displayName;
