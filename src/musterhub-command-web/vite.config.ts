import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5193,
    proxy: {
      '/api': 'http://localhost:5188',
      // SignalR negotiates over plain HTTP first, then upgrades to a
      // WebSocket -- ws: true so Vite's proxy forwards the upgrade too, not
      // just the initial negotiate request.
      '/hubs': { target: 'http://localhost:5188', ws: true },
    },
  },
})
