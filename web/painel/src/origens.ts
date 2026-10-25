/** De onde saiu uma linha dentro do processo: o componente, identificado pela categoria do logger. */
export interface Origem {
  /** Texto curto do selo. */
  rotulo: string;
  /** Explicação no tooltip do selo. */
  descricao: string;
}

/**
 * Categoria do logger (nome da classe, sem namespace) → componente. As colunas são processos; desde as fases 3 e 4,
 * um processo tem mais de um componente (o Producer tem cenários, API e outbox; o Consumer, duas filas).
 * Categoria que não está aqui (ex.: Microsoft.Hosting) fica sem selo.
 */
const origens: Record<string, Origem> = {
  // Producer
  EnvioDePropostas: { rotulo: 'cenário', descricao: 'Envio direto para a fila propostas (cenários das fases 1 e 3)' },
  PublicacaoDeApolices: { rotulo: 'cenário', descricao: 'Publicação direta no tópico apolice-emitida (fase 2)' },
  EnvioDeEndossos: { rotulo: 'cenário', descricao: 'Envio de endossos para a fila endossos (fase 3)' },
  EnvioSemOutbox: { rotulo: 'sem outbox', descricao: 'Dual write: grava no banco e publica sem outbox (fase 4)' },
  InspecaoDeEntidades: { rotulo: 'operação', descricao: 'Peek em filas, assinaturas e DLQs; leitura das regras' },
  ReprocessamentoDaDlq: { rotulo: 'operação', descricao: 'Reprocessamento da DLQ' },
  ApiDePropostas: { rotulo: 'API', descricao: 'API REST /api/propostas: grava a proposta e o outbox numa transação (fase 4)' },
  Outbox: { rotulo: 'outbox', descricao: 'Operação do outbox: estado e simulação de indisponibilidade' },
  DespachanteDoOutbox: { rotulo: 'outbox', descricao: 'Despachante: publica no Service Bus o que a API gravou (fase 4)' },
  MigracaoDoBanco: { rotulo: 'banco', descricao: 'Migrations do EF Core na partida' },
  // Consumer
  Worker: { rotulo: 'propostas', descricao: 'Fila propostas: análise, emissão da apólice ou recusa' },
  ProcessadorDeEndossos: { rotulo: 'endossos', descricao: 'Fila endossos: sessions, session state e deferral (fase 3)' },
};

export function origemDaCategoria(categoria: string): Origem | null {
  return origens[categoria] ?? null;
}
