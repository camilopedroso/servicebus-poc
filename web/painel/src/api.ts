import type { App } from './tipos';

/** Erro de um endpoint, com a explicação do ProblemDetails quando houver. */
export class ErroDaApi extends Error {}

interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

/** Resposta completa, para cenários que mostram status e cabeçalhos (API REST da fase 4). */
export interface RespostaDetalhada<T> {
  status: number;
  cabecalhos: Headers;
  dados: T;
}

/**
 * Chama um app .NET pelo proxy do Vite (/api/producer, /api/consumer, /api/cobranca, /api/notificacao).
 * Respostas de erro viram ErroDaApi com o texto que o painel mostra ao usuário.
 */
export async function chamar<T>(app: App, metodo: 'GET' | 'POST', caminho: string, corpo?: unknown): Promise<T> {
  return (await chamarDetalhado<T>(app, metodo, caminho, corpo)).dados;
}

/** Como chamar, mas aceita cabeçalhos extras e devolve também o status e os cabeçalhos da resposta. */
export async function chamarDetalhado<T>(
  app: App,
  metodo: 'GET' | 'POST',
  caminho: string,
  corpo?: unknown,
  cabecalhosExtras?: Record<string, string>,
): Promise<RespostaDetalhada<T>> {
  let resposta: Response;
  try {
    resposta = await fetch(`/api/${app}${caminho}`, {
      method: metodo,
      headers: {
        ...(corpo === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...cabecalhosExtras,
      },
      body: corpo === undefined ? undefined : JSON.stringify(corpo),
    });
  } catch {
    throw new ErroDaApi(`Sem resposta do ${nomeDoApp[app]}. Confira se ele está rodando.`);
  }

  // O proxy do Vite responde 502/504 sem corpo JSON quando o app está fora do ar.
  const texto = await resposta.text();
  const dados = texto ? tentarJson(texto) : undefined;

  if (!resposta.ok) {
    const problema = dados as ProblemDetails | undefined;
    const errosDeCampo = problema?.errors ? Object.values(problema.errors).flat().join(' ') : '';
    throw new ErroDaApi(
      errosDeCampo ||
        problema?.detail ||
        problema?.title ||
        `O ${nomeDoApp[app]} respondeu ${resposta.status}. Confira se ele está rodando.`,
    );
  }
  return { status: resposta.status, cabecalhos: resposta.headers, dados: dados as T };
}

function tentarJson(texto: string): unknown {
  try {
    return JSON.parse(texto);
  } catch {
    return undefined;
  }
}

export const nomeDoApp: Record<App, string> = {
  producer: 'Producer',
  consumer: 'Consumer',
  cobranca: 'Cobrança',
  notificacao: 'Notificação',
};
