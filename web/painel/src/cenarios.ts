import { chamar } from './api';

/** Campos que um cenário pode pedir. Texto vazio significa "sortear" no Producer. */
export interface Valores {
  quantidade: string;
  idade: string;
  capitalSegurado: string;
  plano: string;
  simularFalha: boolean;
  assinatura: string;
}

export type Campo = keyof Valores;

export interface Cenario {
  id: string;
  fase: 1 | 2;
  titulo: string;
  /** O conceito de Service Bus que o cenário exercita. */
  mostra: string;
  /** O que deve aparecer nas colunas de log. */
  esperado: string;
  campos: Campo[];
  padroes?: Partial<Valores>;
  /** Chama o Producer e devolve um resumo curto do resultado. */
  executar: (valores: Valores) => Promise<string>;
}

export const valoresIniciais: Valores = {
  quantidade: '10',
  idade: '',
  capitalSegurado: '',
  plano: '',
  simularFalha: false,
  assinatura: 'cobranca',
};

export const planos = ['Individual', 'Familiar', 'Empresarial'];
export { assinaturas } from './assinaturas';

const numeroOuNulo = (texto: string) => (texto.trim() === '' ? null : Number(texto));
const textoOuNulo = (texto: string) => (texto === '' ? null : texto);
const plural = (n: number, singular: string, varias: string) => `${n} ${n === 1 ? singular : varias}`;

const enviarProposta = async (v: Valores) => {
  const proposta = await chamar<{ propostaId: string; idade: number }>('producer', 'POST', '/propostas', {
    idade: numeroOuNulo(v.idade),
    capitalSegurado: numeroOuNulo(v.capitalSegurado),
    plano: textoOuNulo(v.plano),
    simularFalha: v.simularFalha,
  });
  return `Proposta ${proposta.propostaId.slice(0, 8)} enviada com idade ${proposta.idade}.`;
};

const publicarApolice = async (v: Valores) => {
  const apolice = await chamar<{ apoliceId: string; capitalSegurado: number }>('producer', 'POST', '/apolices', {
    capitalSegurado: numeroOuNulo(v.capitalSegurado),
    plano: textoOuNulo(v.plano),
  });
  return `Apólice ${apolice.apoliceId.slice(0, 8)} publicada com capital ${apolice.capitalSegurado.toLocaleString('pt-BR')}.`;
};

const espiar = (caminho: string, onde: string) => async () => {
  const mensagens = await chamar<unknown[]>('producer', 'GET', caminho);
  return `${plural(mensagens.length, 'mensagem', 'mensagens')} em ${onde}.`;
};

export const cenarios: Cenario[] = [
  {
    id: 'lote',
    fase: 1,
    titulo: 'Enviar lote de propostas válidas',
    mostra: 'Envio em batch: várias mensagens numa única operação de rede.',
    esperado:
      'O Consumer aprova cada proposta e publica a apólice. Cobrança e notificação recebem todas; alto-valor, só as de capital acima de 1 milhão.',
    campos: ['quantidade'],
    executar: async (v) => {
      const { propostas } = await chamar<{ propostas: string[] }>('producer', 'POST', '/propostas/lote', {
        quantidade: Number(v.quantidade),
      });
      return `${plural(propostas.length, 'proposta enviada', 'propostas enviadas')} para a fila.`;
    },
  },
  {
    id: 'proposta',
    fase: 1,
    titulo: 'Enviar uma proposta sob medida',
    mostra: 'Você escolhe os campos que o Consumer e os filtros das assinaturas avaliam.',
    esperado: 'Até 80 anos, aprovada. Acima de 80, vai para a DLQ. Com falha simulada, 5 entregas e DLQ.',
    campos: ['idade', 'capitalSegurado', 'plano', 'simularFalha'],
    executar: enviarProposta,
  },
  {
    id: 'recusada',
    fase: 1,
    titulo: 'Proposta recusada por idade',
    mostra: 'Regra de negócio violada não é erro técnico: tentar de novo dá o mesmo resultado.',
    esperado: 'O Consumer manda direto para a DLQ com motivo RegraDeNegocio. Confira em "Espiar a DLQ da fila".',
    campos: ['idade', 'capitalSegurado', 'plano'],
    padroes: { idade: '85' },
    executar: enviarProposta,
  },
  {
    id: 'falha',
    fase: 1,
    titulo: 'Falha transitória até a DLQ',
    mostra: 'Abandon devolve a mensagem para a fila na hora, sem backoff.',
    esperado: 'O Consumer registra 5 entregas seguidas e o broker move a mensagem para a DLQ com MaxDeliveryCountExceeded.',
    campos: ['idade', 'capitalSegurado', 'plano', 'simularFalha'],
    padroes: { simularFalha: true },
    executar: enviarProposta,
  },
  {
    id: 'espiar-fila',
    fase: 1,
    titulo: 'Espiar a fila propostas',
    mostra: 'Peek lê as mensagens sem travar nem consumir.',
    esperado: 'Com o Consumer no ar a fila costuma estar vazia. Pare o Consumer e envie um lote para ver mensagens acumuladas.',
    campos: [],
    executar: espiar('/filas/propostas/mensagens?dlq=false', 'propostas'),
  },
  {
    id: 'espiar-dlq',
    fase: 1,
    titulo: 'Espiar a DLQ da fila',
    mostra: 'Mensagens mortas guardam o motivo e a descrição de quem as mandou para lá.',
    esperado: 'A coluna do Producer lista cada mensagem com DeadLetterReason e quantas entregas teve.',
    campos: [],
    executar: espiar('/filas/propostas/mensagens?dlq=true', 'propostas/$DeadLetterQueue'),
  },
  {
    id: 'reprocessar',
    fase: 1,
    titulo: 'Reprocessar a DLQ',
    mostra: 'Reenvia a cópia antes de completar na DLQ: no pior caso duplica, nunca perde.',
    esperado: 'A DLQ esvazia e o Consumer processa de novo. As recusadas por idade voltam para a DLQ.',
    campos: [],
    executar: async () => {
      const { movidas } = await chamar<{ movidas: number }>('producer', 'POST', '/filas/propostas/dlq/reprocessar');
      return `${plural(movidas, 'mensagem devolvida', 'mensagens devolvidas')} para a fila.`;
    },
  },
  {
    id: 'apolice',
    fase: 2,
    titulo: 'Publicar apólice direto no tópico',
    mostra: 'Testa só os filtros das assinaturas, sem passar pela fila nem pelo Consumer.',
    esperado: 'Cobrança e notificação recebem. Alto-valor só recebe se o capital passar de 1 milhão.',
    campos: ['capitalSegurado', 'plano'],
    executar: publicarApolice,
  },
  {
    id: 'apolice-alto-valor',
    fase: 2,
    titulo: 'Publicar apólice de alto valor',
    mostra: 'Filtro SQL sobre application properties, com action que acrescenta uma propriedade.',
    esperado: 'As três assinaturas recebem. Em alto-valor a prioridade chega como "alta", colocada pela action da regra.',
    campos: ['capitalSegurado', 'plano'],
    padroes: { capitalSegurado: '1500000' },
    executar: publicarApolice,
  },
  {
    id: 'espiar-assinatura',
    fase: 2,
    titulo: 'Espiar uma assinatura',
    mostra: 'Cada assinatura é uma fila própria com a sua cópia das mensagens.',
    esperado: 'Desligue a assinatura no seletor da coluna Assinantes e publique apólices para vê-las acumuladas aqui.',
    campos: ['assinatura'],
    executar: (v) => espiar(`/assinaturas/${v.assinatura}/mensagens?dlq=false`, v.assinatura)(),
  },
  {
    id: 'dlq-assinatura',
    fase: 2,
    titulo: 'Espiar a DLQ de uma assinatura',
    mostra: 'Cada assinatura tem a sua própria dead-letter queue.',
    esperado: 'Normalmente vazia: os assinantes só mandam para a DLQ quando o corpo não é uma ApoliceEmitida.',
    campos: ['assinatura'],
    executar: (v) => espiar(`/assinaturas/${v.assinatura}/mensagens?dlq=true`, `${v.assinatura}/$DeadLetterQueue`)(),
  },
  {
    id: 'regras',
    fase: 2,
    titulo: 'Ler as regras das assinaturas',
    mostra: 'Consulta no broker os filtros e actions que o Bicep aplicou.',
    esperado: 'Uma regra por assinatura: correlation em cobrança, TrueFilter em notificação, SQL com action em alto-valor.',
    campos: [],
    executar: async () => {
      const regras = await chamar<unknown[]>('producer', 'GET', '/assinaturas/regras');
      return `Regras lidas de ${plural(regras.length, 'assinatura', 'assinaturas')}. Veja a coluna do Producer.`;
    },
  },
];
