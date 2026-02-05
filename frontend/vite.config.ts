/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { execSync } from 'child_process'

// Get version from environment variable (CI) or git tag (local dev)
function getAppVersion(): string {
  // CI 環境透過 build-arg 傳入
  if (process.env.VITE_APP_VERSION) {
    return process.env.VITE_APP_VERSION
  }
  // 本機開發從 git tag 讀取
  try {
    return execSync('git describe --tags --always').toString().trim()
  } catch {
    return 'dev'
  }
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  define: {
    __APP_VERSION__: JSON.stringify(getAppVersion()),
  },
  server: {
    host: true,
    port: 3000,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
  },
})
