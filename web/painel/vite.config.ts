import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Cada app .NET tem porta fixa (Urls no appsettings.json). O proxy deixa tudo na mesma origem do painel,
// então os backends não precisam de CORS.
const apps = {
  producer: 'http://localhost:5180',
  consumer: 'http://localhost:5181',
  assinantes: 'http://localhost:5182',
};

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: Object.fromEntries(
      Object.entries(apps).map(([nome, alvo]) => [
        `/api/${nome}`,
        { target: alvo, changeOrigin: true, rewrite: (caminho: string) => caminho.replace(`/api/${nome}`, '') },
      ]),
    ),
  },
});
