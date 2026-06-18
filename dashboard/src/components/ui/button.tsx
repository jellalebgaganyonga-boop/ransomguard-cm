import * as React from 'react';
import { Slot } from '@radix-ui/react-slot';
import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from '@/lib/utils';

/**
 * Button — Atom #1 (Atomic Design inventory, Design Phase Day 7-8 §1).
 *
 * Variants map directly to cmp.button.* design tokens (Design Phase Day 6 §4.1):
 * - primary   -> cmp.button.primary.*   (main CTA, e.g., "Se connecter")
 * - secondary -> cmp.button.secondary.* (e.g., "Annuler", "Marquer FP")
 * - danger    -> cmp.button.danger.*    (e.g., "Isoler le poste")
 * - ghost     -> cmp.button.ghost.*     (e.g., "Voir détails →")
 *
 * Sizes map to cmp.button.size.{sm,md,lg}.
 *
 * Per WCAG 2.5.8 (target size, new in 2.2): even the `sm` size keeps a
 * minimum 32px height (24px content + padding), and `icon` size is
 * exactly 40x40 to exceed the 24x24 CSS px minimum with margin for error.
 */

const buttonVariants = cva(
  [
    'inline-flex items-center justify-center gap-2 whitespace-nowrap',
    'rounded-md text-sm font-medium transition-colors duration-fast ease-standard',
    'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-border-focus focus-visible:ring-offset-2',
    'disabled:pointer-events-none disabled:opacity-60',
    '[&_svg]:pointer-events-none [&_svg]:size-4 [&_svg]:shrink-0',
  ],
  {
    variants: {
      variant: {
        primary:
          'bg-primary text-primary-on shadow-sm hover:bg-primary-hover active:bg-primary-active disabled:bg-disabled-bg disabled:text-disabled-text',
        secondary:
          'border border-border-default bg-transparent text-text-primary hover:bg-hover-bg disabled:text-disabled-text',
        danger:
          'bg-severity-critical text-white shadow-sm hover:bg-severity-critical-border disabled:bg-disabled-bg disabled:text-disabled-text',
        ghost: 'bg-transparent text-text-primary hover:bg-hover-bg disabled:text-disabled-text',
      },
      size: {
        sm: 'h-8 px-3 text-xs',
        md: 'h-10 px-4 text-sm',
        lg: 'h-12 px-6 text-base',
        icon: 'h-10 w-10',
      },
    },
    defaultVariants: {
      variant: 'primary',
      size: 'md',
    },
  }
);

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  /** Render as a different element (e.g., Link) while keeping button styling. */
  asChild?: boolean;
  /** Show a loading spinner and disable interaction. */
  isLoading?: boolean;
}

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  (
    { className, variant, size, asChild = false, isLoading = false, disabled, children, ...props },
    ref
  ) => {
    const Comp = asChild ? Slot : 'button';
    return (
      <Comp
        ref={ref}
        className={cn(buttonVariants({ variant, size }), className)}
        disabled={disabled || isLoading}
        aria-busy={isLoading || undefined}
        {...props}
      >
        {isLoading && (
          <svg
            className="size-4 animate-spin"
            viewBox="0 0 24 24"
            fill="none"
            aria-hidden="true"
          >
            <circle
              className="opacity-25"
              cx="12"
              cy="12"
              r="10"
              stroke="currentColor"
              strokeWidth="4"
            />
            <path
              className="opacity-75"
              fill="currentColor"
              d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
            />
          </svg>
        )}
        {children}
      </Comp>
    );
  }
);
Button.displayName = 'Button';
