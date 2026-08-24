import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // Target HTTPS directly: the backend's UseHttpsRedirection() 307-redirects plain HTTP
      // requests, which the proxy won't follow. secure:false trusts the local dev certificate.
      '/api': {
        target: process.env.VITE_API_TARGET ?? 'https://localhost:7166',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
