import { defineConfig, mergeConfig } from 'vitest/config';
import viteConfig from './vite.config';

// Per ADR-FE-008: Vitest for "Small" tests (Google testing taxonomy)
// - Pure functions, validators, hooks (mocked deps)
// - No real network, no real DOM beyond jsdom
// - Target: <5s for full Small suite

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      globals: true,
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
      css: false,
      coverage: {
        provider: 'v8',
        reporter: ['text', 'html', 'lcov'],
        exclude: [
          'node_modules/',
          'src/test/',
          '**/*.stories.tsx',
          '**/*.config.*',
          'dist/',
        ],
        // Per Master Plan testing strategy: Small tests target high coverage
        // on pure logic (validators, formatters, store reducers)
        thresholds: {
          'src/lib/**': { statements: 90, branches: 85 },
          'src/stores/**': { statements: 85, branches: 80 },
        },
      },
      exclude: [
        '**/node_modules/**',
        '**/dist/**',
        '**/e2e/**', // Playwright tests live separately
        '**/*.stories.tsx',
      ],
    },
  })
);
