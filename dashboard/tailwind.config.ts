import type { Config } from 'tailwindcss';
import tailwindcssAnimate from 'tailwindcss-animate';

// Per ADR-FE-002 (shadcn/ui + Tailwind) and ADR-FE-007 (Style Dictionary).
// All values reference CSS custom properties generated in src/styles/tokens.css
// so design changes flow from design-tokens/*.json -> tokens.css -> here,
// without editing this file for routine palette/spacing changes.

export default {
  darkMode: 'class', // Reserved for Sprint 8+; Sprint 7 ships light theme only
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        // Surfaces & backgrounds
        canvas: 'var(--sys-color-background-canvas)',
        elevated: 'var(--sys-color-background-elevated)',
        scrim: 'var(--sys-color-background-scrim)',
        'surface-default': 'var(--sys-color-surface-default)',
        'surface-subtle': 'var(--sys-color-surface-subtle)',
        'surface-muted': 'var(--sys-color-surface-muted)',
        'surface-inverse': 'var(--sys-color-surface-inverse)',

        // Text
        'text-primary': 'var(--sys-color-text-primary)',
        'text-secondary': 'var(--sys-color-text-secondary)',
        'text-tertiary': 'var(--sys-color-text-tertiary)',
        'text-disabled': 'var(--sys-color-text-disabled)',
        'text-inverse': 'var(--sys-color-text-inverse)',
        'text-link': 'var(--sys-color-text-link)',

        // Primary / brand
        primary: 'var(--sys-color-primary)',
        'primary-hover': 'var(--sys-color-primary-hover)',
        'primary-active': 'var(--sys-color-primary-active)',
        'primary-subtle': 'var(--sys-color-primary-subtle)',
        'primary-on': 'var(--sys-color-primary-on)',

        // Severity (security domain semantic — core to this product)
        'severity-critical': 'var(--sys-color-severity-critical)',
        'severity-critical-subtle': 'var(--sys-color-severity-critical-subtle)',
        'severity-critical-border': 'var(--sys-color-severity-critical-border)',
        'severity-high': 'var(--sys-color-severity-high)',
        'severity-high-subtle': 'var(--sys-color-severity-high-subtle)',
        'severity-high-border': 'var(--sys-color-severity-high-border)',
        'severity-medium': 'var(--sys-color-severity-medium)',
        'severity-medium-subtle': 'var(--sys-color-severity-medium-subtle)',
        'severity-medium-border': 'var(--sys-color-severity-medium-border)',
        'severity-low': 'var(--sys-color-severity-low)',
        'severity-low-subtle': 'var(--sys-color-severity-low-subtle)',
        'severity-low-border': 'var(--sys-color-severity-low-border)',
        'severity-info': 'var(--sys-color-severity-info)',
        'severity-info-subtle': 'var(--sys-color-severity-info-subtle)',
        'severity-info-border': 'var(--sys-color-severity-info-border)',

        // Status (alert/agent lifecycle)
        'status-new': 'var(--sys-color-status-new)',
        'status-in-progress': 'var(--sys-color-status-in-progress)',
        'status-resolved': 'var(--sys-color-status-resolved)',
        'status-closed': 'var(--sys-color-status-closed)',
        'status-acknowledged': 'var(--sys-color-status-acknowledged)',
        'status-isolated': 'var(--sys-color-status-isolated)',
        'status-online': 'var(--sys-color-status-online)',
        'status-offline': 'var(--sys-color-status-offline)',
        'status-stale': 'var(--sys-color-status-stale)',

        // Priority score bands (Defender XDR pattern)
        'priority-high': 'var(--sys-color-priority-high)',
        'priority-medium': 'var(--sys-color-priority-medium)',
        'priority-low': 'var(--sys-color-priority-low)',

        // Feedback
        success: 'var(--sys-color-feedback-success)',
        'success-subtle': 'var(--sys-color-feedback-success-subtle)',
        'success-border': 'var(--sys-color-feedback-success-border)',
        warning: 'var(--sys-color-feedback-warning)',
        'warning-subtle': 'var(--sys-color-feedback-warning-subtle)',
        'warning-border': 'var(--sys-color-feedback-warning-border)',
        error: 'var(--sys-color-feedback-error)',
        'error-subtle': 'var(--sys-color-feedback-error-subtle)',
        'error-border': 'var(--sys-color-feedback-error-border)',
        info: 'var(--sys-color-feedback-info)',
        'info-subtle': 'var(--sys-color-feedback-info-subtle)',
        'info-border': 'var(--sys-color-feedback-info-border)',

        // Borders
        'border-default': 'var(--sys-color-border-default)',
        'border-subtle': 'var(--sys-color-border-subtle)',
        'border-strong': 'var(--sys-color-border-strong)',
        'border-focus': 'var(--sys-color-border-focus)',
        'border-error': 'var(--sys-color-border-error)',

        // Interactive states
        'hover-bg': 'var(--sys-color-interactive-hover-bg)',
        'active-bg': 'var(--sys-color-interactive-active-bg)',
        'selected-bg': 'var(--sys-color-interactive-selected-bg)',
        'disabled-bg': 'var(--sys-color-interactive-disabled-bg)',
        'disabled-text': 'var(--sys-color-interactive-disabled-text)',
      },

      spacing: {
        // Bridge ref.spacing.* into Tailwind's spacing scale (additive,
        // does not remove Tailwind defaults which remain available)
        '0': 'var(--ref-spacing-0)',
        '1': 'var(--ref-spacing-1)',
        '2': 'var(--ref-spacing-2)',
        '3': 'var(--ref-spacing-3)',
        '4': 'var(--ref-spacing-4)',
        '5': 'var(--ref-spacing-5)',
        '6': 'var(--ref-spacing-6)',
        '8': 'var(--ref-spacing-8)',
        '10': 'var(--ref-spacing-10)',
        '12': 'var(--ref-spacing-12)',
        '16': 'var(--ref-spacing-16)',
        '20': 'var(--ref-spacing-20)',
        '24': 'var(--ref-spacing-24)',
      },

      borderRadius: {
        none: 'var(--ref-radius-none)',
        sm: 'var(--ref-radius-sm)',
        md: 'var(--ref-radius-md)',
        lg: 'var(--ref-radius-lg)',
        xl: 'var(--ref-radius-xl)',
        full: 'var(--ref-radius-full)',
      },

      borderWidth: {
        '0': 'var(--ref-border-width-0)',
        DEFAULT: 'var(--ref-border-width-1)',
        '2': 'var(--ref-border-width-2)',
        '4': 'var(--ref-border-width-4)',
      },

      fontFamily: {
        sans: ['var(--ref-font-family-sans)'],
        mono: ['var(--ref-font-family-mono)'],
      },

      fontSize: {
        xs: 'var(--ref-font-size-xs)',
        sm: 'var(--ref-font-size-sm)',
        base: 'var(--ref-font-size-md)',
        lg: 'var(--ref-font-size-lg)',
        xl: 'var(--ref-font-size-xl)',
        '2xl': 'var(--ref-font-size-2xl)',
        '3xl': 'var(--ref-font-size-3xl)',
        '4xl': 'var(--ref-font-size-4xl)',
      },

      fontWeight: {
        regular: 'var(--ref-font-weight-regular)',
        medium: 'var(--ref-font-weight-medium)',
        semibold: 'var(--ref-font-weight-semibold)',
        bold: 'var(--ref-font-weight-bold)',
      },

      lineHeight: {
        tight: 'var(--ref-line-height-tight)',
        normal: 'var(--ref-line-height-normal)',
        relaxed: 'var(--ref-line-height-relaxed)',
      },

      letterSpacing: {
        tight: 'var(--ref-letter-spacing-tight)',
        normal: 'var(--ref-letter-spacing-normal)',
        wide: 'var(--ref-letter-spacing-wide)',
        wider: 'var(--ref-letter-spacing-wider)',
      },

      boxShadow: {
        none: 'var(--ref-shadow-none)',
        sm: 'var(--ref-shadow-sm)',
        md: 'var(--ref-shadow-md)',
        lg: 'var(--ref-shadow-lg)',
        xl: 'var(--ref-shadow-xl)',
      },

      transitionDuration: {
        instant: 'var(--ref-duration-instant)',
        fast: 'var(--ref-duration-fast)',
        normal: 'var(--ref-duration-normal)',
        slow: 'var(--ref-duration-slow)',
        slower: 'var(--ref-duration-slower)',
      },

      transitionTimingFunction: {
        standard: 'var(--ref-easing-standard)',
        emphasized: 'var(--ref-easing-emphasized)',
        bounce: 'var(--ref-easing-bounce)',
      },

      zIndex: {
        below: 'var(--ref-zindex-below)',
        docked: 'var(--ref-zindex-docked)',
        dropdown: 'var(--ref-zindex-dropdown)',
        sticky: 'var(--ref-zindex-sticky)',
        modal: 'var(--ref-zindex-modal)',
        popover: 'var(--ref-zindex-popover)',
        toast: 'var(--ref-zindex-toast)',
        tooltip: 'var(--ref-zindex-tooltip)',
      },

      screens: {
        // Per Definition Phase Day 4 §1 — 320 / 640 / 768 / 1024 / 1280 / 1440
        xs: '320px',
        // sm, md, lg, xl, 2xl use Tailwind defaults (640/768/1024/1280) which
        // already align; only 2xl is overridden to match 1440 design spec
        '2xl': '1440px',
      },
    },
  },
  plugins: [tailwindcssAnimate],
} satisfies Config;
