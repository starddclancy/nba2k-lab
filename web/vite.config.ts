import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The bundle is served from an embedded resource host inside the WebView2 shell, so assets
// use absolute paths against that virtual origin and everything lands in one directory.
export default defineConfig({
    base: '/',
    plugins: [react()],
    build: {
        outDir: 'dist',
        emptyOutDir: true,
        assetsDir: 'assets',
        sourcemap: false,
        chunkSizeWarningLimit: 2000,
    },
});
