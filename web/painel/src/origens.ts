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
  EnvioDePropostas: { rotulo: 'cenário', descricao: 'Publicação direta no tópico proposta-recebida, sem outbox (cenários das fases 1 e 3)' },
  PublicacaoDeApolices: { rotulo: 'cenário', descricao: 'Publicação direta no tópico apolice-emitida (fase 2)' },
  EnvioDeEndossos: { rotulo: 'cenário', descricao: 'Envio de endossos para a fila endossos (fase 3)' },
  EnvioSemOutbox: { rotulo: 'sem outbox', descricao: 'Dual write: grava no banco e publica sem outbox (fase 4)' },
  InspecaoDeEntidades: { rotulo: 'operação', descricao: 'Peek em filas, assinaturas e DLQs; leitura das regras' },
  ReprocessamentoDaDlq: { rotulo: 'operação', descricao: 'Reprocessamento da DLQ' },
  PropostasController: { rotulo: 'API', descricao: 'API REST /api/propostas: grava a proposta e o outbox numa transação (fase 4)' },
  OutboxController: { rotulo: 'outbox', descricao: 'Operação do outbox: pendentes e publicadas' },
  SimulacaoDoOutboxController: { rotulo: 'outbox', descricao: 'Demonstração: simula o Service Bus fora do ar para o despachante (fase 4)' },
  DespachanteDoOutbox: { rotulo: 'outbox', descricao: 'Despachante: publica no Service Bus o que a API gravou (fase 4)' },
  ProcessadorDeDesfechos: {
    rotulo: 'desfecho',
    descricao: 'Assinaturas status-aprovada e status-recusada: grava Aprovada ou Recusada na proposta da API',
  },
  MigracaoDoBanco: { rotulo: 'banco', descricao: 'Migrations do EF Core na partida' },
  // Consumer
  Worker: { rotulo: 'propostas', descricao: 'Fila subscricao-entrada (vinda do tópico proposta-recebida): análise, emissão da apólice ou recusa' },
  ProcessadorDeEndossos: { rotulo: 'endossos', descricao: 'Fila endossos: sessions, session state e deferral (fase 3)' },
};

export function origemDaCategoria(categoria: string): Origem | null {
  return origens[categoria] ?? null;
}
