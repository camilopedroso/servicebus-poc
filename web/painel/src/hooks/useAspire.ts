import { useEffect, useState } from 'react';

/** Endereço do Aspire Dashboard local (iniciar.ps1 -ComAspire). */
export const enderecoDoAspire = 'http://localhost:18888';

const intervalo = 30_000;

/**
 * Diz se o Aspire Dashboard está no ar, para as linhas de log oferecerem "abrir o trace" nele.
 * A consulta passa pelo proxy do Vite (/api/aspire), então não esbarra em CORS.
 */
export function useAspire(): boolean {
  const [disponivel, setDisponivel] = useState(false);

  useEffect(() => {
    let ativo = true;
    const verificar = async () => {
      try {
        const resposta = await fetch('/api/aspire/api/telemetry/resources');
        if (ativo) setDisponivel(resposta.ok);
      } catch {
        if (ativo) setDisponivel(false);
      }
    };
    verificar();
    const timer = setInterval(verificar, intervalo);
    return () => {
      ativo = false;
      clearInterval(timer);
    };
  }, []);

  return disponivel;
}
