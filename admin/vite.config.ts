/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const apiTarget = process.env.VITE_API_TARGET ?? 'http://localhost:5080'
// Opening the admin from a phone by IP works as is; a host name (Tailscale *.ts.net, a name in the router) must be allowed.
const allowedHosts = ['.ts.net', ...(process.env.VITE_ALLOWED_HOSTS ?? '').split(',').map((h) => h.trim()).filter(Boolean)]

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    allowedHosts,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: false },
      '/hubs': { target: apiTarget, ws: true, changeOrigin: false },
      '/hangfire': { target: apiTarget, changeOrigin: false },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './vitest.setup.ts',
    css: false,
  },
})
