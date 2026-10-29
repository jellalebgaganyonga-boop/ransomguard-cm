import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tsconfigPaths from 'vite-tsconfig-paths';
import path from 'path';

// Per ADR-FE-001: React 18 + TypeScript strict + Vite
// Per Definition Phase Day 5 (STRIDE WT2.1, WT2.4): API proxy in dev to avoid
// CORS complexity; production uses nginx reverse proxy (deployment/grid/nginx)

export default defineConfig({
  plugins: [react(), tsconfigPaths()],

  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },

  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      // Forward /api/* to GRID backend during development
      // Production: nginx handles this (deployment/grid/nginx/conf.d/dashboard.conf)
      '/api': {
        // VITE_PROXY_TARGET lets CI point the proxy at a closed local port:
        // the GRID server is only reachable inside the Tailscale network, so on
        // a runner any request that escapes the E2E mocks would hang on a TCP
        // connect timeout instead of failing immediately.
        target: process.env.VITE_PROXY_TARGET ?? 'https://100.85.208.124:8443',
        changeOrigin: true,
        // Validate the GRID's certificate, always. A GRID on the internal PKI is trusted by
        // giving this one Node process its CA -- NODE_EXTRA_CA_CERTS=deployment/grid/pki/ca.crt --
        // never by turning the check off, and never by adding the CA to the machine store.
        // Plain-http targets (the local uvicorn, CI's closed port) are unaffected.
        secure: true,
      },
    },
  },

  build: {
    target: 'es2022',
    sourcemap: true,
    // Per ADR-FE-002: no inline scripts in production build (CSP script-src 'self')
    rollupOptions: {
      output: {
        manualChunks: {
          'react-vendor': ['react', 'react-dom', 'react-router-dom'],
          'query-vendor': ['@tanstack/react-query'],
          'form-vendor': ['react-hook-form', '@hookform/resolvers', 'zod'],
          'i18n-vendor': ['react-i18next', 'i18next', 'i18next-icu'],
          'chart-vendor': ['recharts'],
          // The French translations, in a chunk of their own and under a name that says what
          // it is. It is fetched only when somebody switches to French, and check-no-french
          // skips it by name: it is the one file in the build that is French on purpose.
          'french-translations': ['./src/i18n/locales/fr.json'],
        },
      },
    },
    // Performance budget per Definition Phase NFRs (FCP target < 2s on 3G)
    chunkSizeWarningLimit: 500,
  },

  // Vitest configuration is in vitest.config.ts (separate file per convention)
});
