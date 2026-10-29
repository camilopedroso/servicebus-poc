import { useState } from 'react';
import { apps, type App } from '../apps';

const CHAVE = 'painel.colunasOcultas';

function lerSalvas(): Set<App> {
  try {
    const texto = localStorage.getItem(CHAVE);
    const ids = texto ? (JSON.parse(texto) as string[]) : [];
    const validos = apps.map((a) => a.id as string);
    return new Set(ids.filter((id) => validos.includes(id)) as App[]);
  } catch {
    // Janela privada ou armazenamento bloqueado: começa com todas visíveis.
    return new Set();
  }
}

/**
 * Quais colunas o usuário escondeu (cinco colunas apertam a tela). Fica guardado no navegador, só para este usuário.
 * Esconder não desconecta: a coluna continua recebendo logs, e a trilha continua vendo as linhas dela.
 */
export function useColunasOcultas() {
  const [ocultas, setOcultas] = useState<Set<App>>(lerSalvas);

  const alternar = (app: App) => {
    setOcultas((atuais) => {
      const novas = new Set(atuais);
      if (novas.has(app)) {
        novas.delete(app);
      } else if (novas.size < apps.length - 1) {
        // Pelo menos uma coluna fica visível.
        novas.add(app);
      }
      try {
        localStorage.setItem(CHAVE, JSON.stringify([...novas]));
      } catch {
        // Sem armazenamento, a escolha vale só até recarregar a página.
      }
      return novas;
    });
  };

  return { ocultas, alternar };
}
