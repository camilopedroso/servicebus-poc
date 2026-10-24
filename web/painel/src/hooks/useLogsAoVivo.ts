import { useCallback, useEffect, useRef, useState } from 'react';
import type { App, EstadoDaConexao, LinhaDeLog, RegistroDeLog } from '../tipos';

const LIMITE_DE_LINHAS = 1_000;

/**
 * Acompanha o GET /logs (SSE) de um app. O EventSource reconecta sozinho quando o app reinicia;
 * a cada conexão o servidor reenvia o histórico, e as linhas já vistas são descartadas pela sequência.
 */
export function useLogsAoVivo(app: App) {
  const [linhas, setLinhas] = useState<LinhaDeLog[]>([]);
  const [estado, setEstado] = useState<EstadoDaConexao>('conectando');
  // Último número de sequência visto por processo, e o processo atual (para marcar reinícios).
  const vistos = useRef(new Map<string, number>());
  const processoAtual = useRef<string | null>(null);

  useEffect(() => {
    const fonte = new EventSource(`/api/${app}/logs`);
    fonte.onopen = () => setEstado('ao-vivo');
    fonte.onerror = () => setEstado('reconectando');
    fonte.addEventListener('log', (evento) => {
      const registro = JSON.parse((evento as MessageEvent<string>).data) as RegistroDeLog;
      const ultimo = vistos.current.get(registro.processo) ?? 0;
      if (registro.sequencia <= ultimo) {
        return;
      }
      vistos.current.set(registro.processo, registro.sequencia);

      const novas: LinhaDeLog[] = [];
      if (processoAtual.current !== null && processoAtual.current !== registro.processo) {
        novas.push({ tipo: 'reinicio', chave: `reinicio-${registro.processo}`, momento: registro.momento });
      }
      processoAtual.current = registro.processo;
      novas.push({ tipo: 'registro', chave: `${registro.processo}-${registro.sequencia}`, registro });

      setLinhas((anteriores) => [...anteriores, ...novas].slice(-LIMITE_DE_LINHAS));
    });
    return () => fonte.close();
  }, [app]);

  const limpar = useCallback(() => setLinhas([]), []);

  return { linhas, estado, limpar };
}
