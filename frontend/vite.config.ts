/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'

// API ve SignalR aynı origin üzerinden proxy'leniyor: tarayıcı açısından tek
// origin olduğu için CORS gerekmez ve ödeme sağlayıcısının form POST'u da
// (/api/payments/...) doğrudan çalışır.
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const target = env.VITE_API_TARGET ?? 'http://localhost:5001'

  return {
    plugins: [react()],
    // Satır içi (boş) PostCSS yapılandırması: Vite'ın üst dizinlerde başka bir
    // projenin postcss/tailwind yapılandırmasını bulup uygulamasını engeller.
    css: { postcss: { plugins: [] } },
    server: {
      port: 5173,
      proxy: {
        '/api': { target, changeOrigin: true },
        '/hubs': { target, changeOrigin: true, ws: true },
        '/health': { target, changeOrigin: true },
      },
    },
    test: {
      environment: 'jsdom',
      globals: true,
      setupFiles: ['./src/test-setup.ts'],
    },
  }
})
