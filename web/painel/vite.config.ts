import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { apps } from './src/apps.ts';

// Cada serviço .NET tem porta fixa (Urls no appsettings.json); a lista fica em src/apps.ts, a mesma das colunas.
// O proxy deixa tudo na mesma origem do painel, então os backends não precisam de CORS.
const alvos: Record<string, string> = Object.fromEntries(apps.map((app) => [app.id, `http://localhost:${app.porta}`]));
// Fase 5: Aspire Dashboard local (iniciar.ps1 -ComAspire). O painel só consulta se ele está no ar.
alvos.aspire = 'http://localhost:18888';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: Object.fromEntries(
      Object.entries(alvos).map(([nome, alvo]) => [
        `/api/${nome}`,
        { target: alvo, changeOrigin: true, rewrite: (caminho: string) => caminho.replace(`/api/${nome}`, '') },
      ]),
    ),
  },
});
