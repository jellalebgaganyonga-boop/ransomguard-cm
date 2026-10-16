import js from '@eslint/js';
import globals from 'globals';
import tseslint from '@typescript-eslint/eslint-plugin';
import tsParser from '@typescript-eslint/parser';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import jsxA11y from 'eslint-plugin-jsx-a11y';

/**
 * ESLint flat config (ESLint 9+) — ADR-FE-001 (TypeScript strict) +
 * ADR-FE-008 (testing/quality gates).
 *
 * jsx-a11y is enabled at recommended level: per Design Phase Day 6 §5
 * (WCAG 2.2 AA validation at the token level), linting catches missing
 * aria-* attributes and non-semantic interactive elements at write time,
 * complementing the runtime axe-core checks in Playwright (ADR-FE-008).
 */

export default [
  { ignores: ['dist', 'node_modules', 'src/types/design-tokens.ts', 'storybook-static'] },
  js.configs.recommended,
  {
    files: ['**/*.{ts,tsx}'],
    languageOptions: {
      globals: {
        ...globals.browser,
        ...globals.node,
      },
      parser: tsParser,
      parserOptions: {
        ecmaVersion: 2022,
        sourceType: 'module',
        ecmaFeatures: { jsx: true },
        // Deliberately NOT setting `project` here: tseslint.configs.recommended
        // (used below) is the non-type-checked rule set and does not require
        // it. Setting `project: './tsconfig.json'` would break linting of
        // root-level config files (vite.config.ts, tailwind.config.ts, this
        // file, etc.) since tsconfig.json's `include` is `["src"]` only —
        // ESLint's typescript parser errors on any linted file outside the
        // referenced tsconfig's program. If type-aware rules are needed later,
        // switch to `tseslint.configs.recommendedTypeChecked` AND list both
        // tsconfig.json and tsconfig.node.json in `project`, plus add a
        // tsconfig covering root config files.
      },
    },
    plugins: {
      '@typescript-eslint': tseslint,
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
      'jsx-a11y': jsxA11y,
    },
    rules: {
      ...tseslint.configs.recommended.rules,
      ...reactHooks.configs.recommended.rules,
      ...jsxA11y.configs.recommended.rules,

      'react-refresh/only-export-components': [
        'warn',
        { allowConstantExport: true },
      ],

      // Per ADR-FE-001 strict mode: surface unused vars as errors, but
      // allow underscore-prefixed args (common for unused event params).
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_' },
      ],

      // Pydantic-style explicit return types are NOT required (would be
      // excessive for React components); rely on inference + strict mode.
      '@typescript-eslint/explicit-function-return-type': 'off',
      '@typescript-eslint/explicit-module-boundary-types': 'off',

      // STRIDE WT5.x — no `any` escape hatches in a security product.
      '@typescript-eslint/no-explicit-any': 'error',
    },
  },
];
