/** Os processos .NET que o painel acompanha. */
export type App = 'producer' | 'consumer' | 'cobranca' | 'notificacao';

/** Uma linha de log como chega do GET /logs (Vida.LogsAoVivo.Logs.RegistroDeLog). */
export interface RegistroDeLog {
  processo: string;
  sequencia: number;
  momento: string;
  nivel: 'Trace' | 'Debug' | 'Information' | 'Warning' | 'Error' | 'Critical';
  categoria: string;
  mensagem: string;
  excecao: string | null;
  ids: Record<string, string>;
  /** Trace do OpenTelemetry (fase 5): o mesmo nos três apps quando a mensagem é do mesmo fluxo. */
  traceId?: string | null;
}

/** Linha exibida: um registro ou o aviso de que o processo foi reiniciado. */
export type LinhaDeLog =
  | { tipo: 'registro'; chave: string; registro: RegistroDeLog }
  | { tipo: 'reinicio'; chave: string; momento: string };

export type EstadoDaConexao = 'conectando' | 'ao-vivo' | 'reconectando';

/** GET /assinaturas de um serviço que assina tópicos (Cobrança, Notificação): só as assinaturas dele. */
export interface EstadoDaAssinatura {
  nome: string;
  topico: string;
  filtros: string[];
  ativa: boolean;
  pendentes: number;
  naDlq: number;
}

/** GET /processadores (Consumer, Cobrança, Notificação): liga/desliga de cada processor do app. */
export interface EstadoDoProcessador {
  nome: string;
  /** Fila ou assinatura que ele lê. */
  entidade: string;
  ativo: boolean;
}
