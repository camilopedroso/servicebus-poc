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
  fase: 1 | 2 | 3;
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
      'O Consumer aprova cada proposta e publica a apólice. Notificação recebe todas. Capital até 1 milhão vai para cobrança; acima disso, para alto-valor (revisão manual), e não é cobrada agora.',
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
    esperado:
      'Até 80 anos, aprovada. Acima de 80, recusada e o cliente é avisado por notificacao-recusa. Com falha simulada, 5 entregas e DLQ.',
    campos: ['idade', 'capitalSegurado', 'plano', 'simularFalha'],
    executar: enviarProposta,
  },
  {
    id: 'recusada',
    fase: 1,
    titulo: 'Proposta recusada por idade',
    mostra:
      'Recusa por regra de negócio é um desfecho, não uma falha: a mensagem é concluída e o fato vira evento. A DLQ fica para o que não dá para processar.',
    esperado:
      'O Consumer conclui a proposta e publica PropostaRecusada no tópico proposta-recusada, na mesma transação. A assinatura notificacao-recusa avisa o cliente. Nada vai para a DLQ.',
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
    id: 'falha-permanente',
    fase: 1,
    titulo: 'Falha irrecuperável (payload inválido)',
    mostra:
      'Mensagem venenosa: o corpo não pode ser lido (idade como texto). Tentar de novo não resolve, então o Consumer faz dead-letter na hora, sem gastar as 5 entregas.',
    esperado:
      'O Consumer registra 1 entrega só e manda para a DLQ com PayloadInvalido. Em "Reprocessar a DLQ", ela fica lá: precisa de correção, não de reenvio.',
    campos: [],
    executar: async () => {
      const { messageId } = await chamar<{ messageId: string }>('producer', 'POST', '/propostas/invalida');
      return `Mensagem ${messageId.slice(0, 8)} enviada com corpo inválido.`;
    },
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
    mostra:
      'Reprocessa só o que estava na DLQ no início e deixa lá o que não tem conserto (payload inválido), para não entrar em loop.',
    esperado:
      'As mensagens voltam para a fila e o Consumer dá um desfecho definitivo: aprova, ou recusa e notifica. A DLQ esvazia.',
    campos: [],
    executar: async () => {
      const { movidas, mantidas } = await chamar<{ movidas: number; mantidas: number }>(
        'producer',
        'POST',
        '/filas/propostas/dlq/reprocessar',
      );
      const ficaram = mantidas > 0 ? ` ${plural(mantidas, 'ficou', 'ficaram')} na DLQ por motivo permanente.` : '';
      return `${plural(movidas, 'mensagem devolvida', 'mensagens devolvidas')} para a fila.${ficaram}`;
    },
  },
  {
    id: 'apolice',
    fase: 2,
    titulo: 'Publicar apólice direto no tópico',
    mostra: 'Testa só os filtros das assinaturas, sem passar pela fila nem pelo Consumer.',
    esperado: 'Notificação sempre recebe. Até 1 milhão, cobrança também; acima disso, alto-valor no lugar da cobrança.',
    campos: ['capitalSegurado', 'plano'],
    executar: publicarApolice,
  },
  {
    id: 'apolice-alto-valor',
    fase: 2,
    titulo: 'Publicar apólice de alto valor',
    mostra: 'Filtros complementares: requerRevisao = TRUE em alto-valor, = FALSE em cobrança. A action acrescenta uma propriedade.',
    esperado: 'Notificação e alto-valor recebem; cobrança não. Em alto-valor a prioridade chega como "alta", colocada pela action da regra.',
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
    esperado: 'Normalmente vazia: os assinantes só mandam para a DLQ quando o corpo não pode ser lido.',
    campos: ['assinatura'],
    executar: (v) => espiar(`/assinaturas/${v.assinatura}/mensagens?dlq=true`, `${v.assinatura}/$DeadLetterQueue`)(),
  },
  {
    id: 'regras',
    fase: 2,
    titulo: 'Ler as regras das assinaturas',
    mostra: 'Consulta no broker os filtros e actions que o Bicep aplicou.',
    esperado:
      'Uma regra por assinatura: SQL em cobrança (requerRevisao = FALSE), TrueFilter em notificação, SQL com action em alto-valor (requerRevisao = TRUE) e TrueFilter em notificacao-recusa (tópico proposta-recusada).',
    campos: [],
    executar: async () => {
      const regras = await chamar<unknown[]>('producer', 'GET', '/assinaturas/regras');
      return `Regras lidas de ${plural(regras.length, 'assinatura', 'assinaturas')}. Veja a coluna do Producer.`;
    },
  },
  {
    id: 'endossos-em-ordem',
    fase: 3,
    titulo: 'Endossos em ordem (sessions)',
    mostra:
      'SessionId = apólice: as versões de cada apólice saem em ordem e uma de cada vez; apólices diferentes rodam em paralelo.',
    esperado:
      'No Consumer, cada apólice abre sua sessão e aplica v1, v2, v3, v4 nessa ordem. As apólices se intercalam entre si, mas nunca uma versão passa outra da mesma apólice.',
    campos: [],
    executar: async () => {
      const { apolices } = await chamar<{ apolices: string[] }>('producer', 'POST', '/endossos/em-ordem', {
        apolices: 3,
        versoes: 4,
      });
      return `${plural(apolices.length, 'apólice', 'apólices')} com 4 endossos cada.`;
    },
  },
  {
    id: 'endossos-fora-de-ordem',
    fase: 3,
    titulo: 'Endossos fora de ordem (deferral)',
    mostra:
      'Session garante a ordem de chegada, não a de negócio. Versão adiantada é adiada (defer) e retomada pelo SequenceNumber guardado no session state.',
    esperado:
      'O Consumer adia cada versão que chega antes da hora. Quando a que falta chega, aplica e retoma as adiadas em sequência, até a v5.',
    campos: [],
    executar: async () => {
      const { ordem } = await chamar<{ ordem: number[] }>('producer', 'POST', '/endossos/fora-de-ordem');
      return `Enviados na ordem ${ordem.map((v) => `v${v}`).join(', ')}.`;
    },
  },
  {
    id: 'endosso-duplicado',
    fase: 3,
    titulo: 'Endosso duplicado (detecção de duplicatas)',
    mostra:
      'O producer reenvia a v1 com o mesmo MessageId. O broker aceita o envio, mas descarta a cópia: ela nunca chega ao consumer.',
    esperado: 'O Producer registra três envios aceitos; o Consumer aplica só v1 e v2, uma vez cada.',
    campos: [],
    executar: async () => {
      const { apoliceId } = await chamar<{ apoliceId: string }>('producer', 'POST', '/endossos/duplicado');
      return `Apólice ${apoliceId.slice(0, 8)}: 3 envios, 2 mensagens na fila.`;
    },
  },
  {
    id: 'falha-backoff',
    fase: 3,
    titulo: 'Falha transitória com backoff (agendamento)',
    mostra:
      'Em vez de Abandon (reentrega na hora), o Consumer conclui e agenda uma cópia para daqui a 2 s, depois 4 s, na mesma transação. A dependência volta na 3ª tentativa.',
    esperado:
      'Duas falhas com espera crescente e aprovação na tentativa 3. Compare com "Falha transitória até a DLQ" da fase 1: lá as 5 entregas acabam em segundos.',
    campos: ['idade', 'capitalSegurado', 'plano'],
    executar: async (v) => {
      const proposta = await chamar<{ propostaId: string }>('producer', 'POST', '/propostas', {
        idade: numeroOuNulo(v.idade),
        capitalSegurado: numeroOuNulo(v.capitalSegurado),
        plano: textoOuNulo(v.plano),
        simularFalha: true,
        retryComBackoff: true,
      });
      return `Proposta ${proposta.propostaId.slice(0, 8)} enviada com falha simulada e retry agendado.`;
    },
  },
];
