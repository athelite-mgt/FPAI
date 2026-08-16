// Vitest's defineConfig is a superset of Vite's and types the `test` block.
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'node:path'

const rootDir = import.meta.dirname

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(rootDir, './src') },
  },
  server: {
    port: 5173,
    // The SPA calls /api/* on its own origin; Vite forwards to the .NET host in development.
    proxy: {
      '/api': {
        target: process.env.VITE_API_TARGET ?? 'http://localhost:5099',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    // e2e/ holds Playwright specs, which must not be collected by Vitest.
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    exclude: ['e2e/**', 'node_modules/**', 'dist/**'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'html'],
      include: ['src/lib/**', 'src/components/**'],
      exclude: ['src/test/**', '**/*.test.*'],
    },
  },
})
