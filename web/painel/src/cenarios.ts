import { chamar, chamarDetalhado } from './api';

/** Campos que um cenário pode pedir. Texto vazio significa "sortear" no serviço. */
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
  fase: 1 | 2 | 3 | 4;
  titulo: string;
  /** O conceito de Service Bus que o cenário exercita. */
  mostra: string;
  /** O que deve aparecer nas colunas de log. */
  esperado: string;
  campos: Campo[];
  padroes?: Partial<Valores>;
  /** Chama os serviços e devolve um resumo curto do resultado. */
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
  const proposta = await chamar<{ propostaId: string; idade: number }>('propostas', 'POST', '/demo/propostas', {
    idade: numeroOuNulo(v.idade),
    capitalSegurado: numeroOuNulo(v.capitalSegurado),
    plano: textoOuNulo(v.plano),
    simularFalha: v.simularFalha,
  });
  return `Proposta ${proposta.propostaId.slice(0, 8)} enviada com idade ${proposta.idade}.`;
};

const publicarApolice = async (v: Valores) => {
  const apolice = await chamar<{ apoliceId: string; capitalSegurado: number }>('subscricao', 'POST', '/demo/apolices', {
    capitalSegurado: numeroOuNulo(v.capitalSegurado),
    plano: textoOuNulo(v.plano),
  });
  return `Apólice ${apolice.apoliceId.slice(0, 8)} publicada com capital ${apolice.capitalSegurado.toLocaleString('pt-BR')}.`;
};

interface PropostaDaApi {
  propostaId: string;
  status: string;
  apoliceId: string | null;
  motivoDaRecusa: string | null;
}

/** Status do GET com o detalhe do desfecho, quando já houver. */
const descreverStatus = (p: PropostaDaApi) => {
  if (p.apoliceId) {
    return `${p.status} (apólice ${p.apoliceId.slice(0, 8)})`;
  }
  if (p.motivoDaRecusa) {
    return `${p.status} (${p.motivoDaRecusa})`;
  }
  return p.status;
};

/** Corpo do POST /api/propostas: a API exige todos os campos, então o painel sorteia os que ficarem vazios. */
const novaProposta = (v: Valores) => ({
  clienteId: `CLI-${Math.floor(1000 + Math.random() * 9000)}`,
  idade: numeroOuNulo(v.idade) ?? Math.floor(18 + Math.random() * 52),
  capitalSegurado: numeroOuNulo(v.capitalSegurado) ?? Math.floor(50 + Math.random() * 1950) * 1000,
  plano: textoOuNulo(v.plano) ?? planos[Math.floor(Math.random() * planos.length)],
});

const esperar = (ms: number) => new Promise((resolver) => setTimeout(resolver, ms));

const consultarProposta = (id: string) => chamar<PropostaDaApi>('propostas', 'GET', `/api/propostas/${id}`);

const espiar = (caminho: string, onde: string) => async () => {
  const mensagens = await chamar<unknown[]>('operacao', 'GET', caminho);
  return `${plural(mensagens.length, 'mensagem', 'mensagens')} em ${onde}.`;
};

export const cenarios: Cenario[] = [
  {
    id: 'lote',
    fase: 1,
    titulo: 'Enviar lote de propostas válidas',
    mostra: 'Envio em batch: várias mensagens numa única operação de rede.',
    esperado:
      'A Subscrição aprova cada proposta e publica a apólice. Notificação recebe todas. Capital até 1 milhão vai para cobrança; acima disso, para alto-valor (revisão manual), e não é cobrada agora.',
    campos: ['quantidade'],
    executar: async (v) => {
      const { propostas } = await chamar<{ propostas: string[] }>('propostas', 'POST', '/demo/propostas/lote', {
        quantidade: Number(v.quantidade),
      });
      return `${plural(propostas.length, 'proposta publicada', 'propostas publicadas')} no tópico proposta-recebida.`;
    },
  },
  {
    id: 'proposta',
    fase: 1,
    titulo: 'Enviar uma proposta sob medida',
    mostra: 'Você escolhe os campos que a Subscrição e os filtros das assinaturas avaliam.',
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
      'A Subscrição grava a recusa e o evento PropostaRecusada no banco dela (inbox + recusa + outbox, numa transação), conclui a mensagem, e o despachante publica no tópico proposta-recusada. A assinatura notificacao-recusa avisa o cliente. Nada vai para a DLQ.',
    campos: ['idade', 'capitalSegurado', 'plano'],
    padroes: { idade: '85' },
    executar: enviarProposta,
  },
  {
    id: 'falha',
    fase: 1,
    titulo: 'Falha transitória até a DLQ',
    mostra: 'Abandon devolve a mensagem para a fila na hora, sem backoff.',
    esperado: 'A Subscrição registra 5 entregas seguidas e o broker move a mensagem para a DLQ com MaxDeliveryCountExceeded.',
    campos: ['idade', 'capitalSegurado', 'plano', 'simularFalha'],
    padroes: { simularFalha: true },
    executar: enviarProposta,
  },
  {
    id: 'falha-permanente',
    fase: 1,
    titulo: 'Falha irrecuperável (payload inválido)',
    mostra:
      'Mensagem venenosa: o corpo não pode ser lido (idade como texto). Tentar de novo não resolve, então a Subscrição faz dead-letter na hora, sem gastar as 5 entregas.',
    esperado:
      'A Subscrição registra 1 entrega só e manda para a DLQ com PayloadInvalido. Em "Reprocessar a DLQ", ela fica lá: precisa de correção, não de reenvio.',
    campos: [],
    executar: async () => {
      const { messageId } = await chamar<{ messageId: string }>('propostas', 'POST', '/demo/propostas/invalida');
      return `Mensagem ${messageId.slice(0, 8)} enviada com corpo inválido.`;
    },
  },
  {
    id: 'espiar-fila',
    fase: 1,
    titulo: 'Espiar a fila subscricao-entrada',
    mostra: 'Peek lê as mensagens sem travar nem consumir. A fila é a caixa de entrada da Subscrição: o tópico proposta-recebida encaminha para ela (forwardTo).',
    esperado: 'Com a Subscrição no ar a fila costuma estar vazia. Pare a Subscrição e envie um lote para ver mensagens acumuladas.',
    campos: [],
    executar: espiar('/filas/subscricao-entrada/mensagens?dlq=false', 'subscricao-entrada'),
  },
  {
    id: 'espiar-dlq',
    fase: 1,
    titulo: 'Espiar a DLQ da fila',
    mostra: 'Mensagens mortas guardam o motivo e a descrição de quem as mandou para lá.',
    esperado: 'A coluna da Operação lista cada mensagem com DeadLetterReason e quantas entregas teve.',
    campos: [],
    executar: espiar('/filas/subscricao-entrada/mensagens?dlq=true', 'subscricao-entrada/$DeadLetterQueue'),
  },
  {
    id: 'reprocessar',
    fase: 1,
    titulo: 'Reprocessar a DLQ',
    mostra:
      'Reprocessa só o que estava na DLQ no início e deixa lá o que não tem conserto (payload inválido), para não entrar em loop.',
    esperado:
      'As mensagens voltam para a fila e a Subscrição dá um desfecho definitivo: aprova, ou recusa e notifica. A DLQ esvazia.',
    campos: [],
    executar: async () => {
      const { movidas, mantidas } = await chamar<{ movidas: number; mantidas: number }>(
        'operacao',
        'POST',
        // Tirar o simularFalha simula "corrigir a causa antes de reprocessar". A Operação não sabe o que ele é.
        '/filas/subscricao-entrada/dlq/reprocessar?removerPropriedade=simularFalha',
      );
      const ficaram = mantidas > 0 ? ` ${plural(mantidas, 'ficou', 'ficaram')} na DLQ por motivo permanente.` : '';
      return `${plural(movidas, 'mensagem devolvida', 'mensagens devolvidas')} para a fila.${ficaram}`;
    },
  },
  {
    id: 'apolice',
    fase: 2,
    titulo: 'Publicar apólice direto no tópico',
    mostra: 'Testa só os filtros das assinaturas, sem passar pela fila nem pela Subscrição.',
    esperado:
      'Notificação sempre recebe. Até 1 milhão, Cobrança também; acima disso, a cópia vai para alto-valor no lugar da cobrança e fica esperando: a squad de Resseguro ainda não tem serviço.',
    campos: ['capitalSegurado', 'plano'],
    executar: publicarApolice,
  },
  {
    id: 'apolice-alto-valor',
    fase: 2,
    titulo: 'Publicar apólice de alto valor',
    mostra: 'Filtros complementares: requerRevisao = TRUE em alto-valor, = FALSE em cobrança. A action acrescenta uma propriedade.',
    esperado:
      'Notificação recebe; Cobrança não. A cópia de alto-valor fica pendente (Resseguro ainda não tem serviço): espie a assinatura alto-valor para ver a prioridade "alta", colocada pela action da regra.',
    campos: ['capitalSegurado', 'plano'],
    padroes: { capitalSegurado: '1500000' },
    executar: publicarApolice,
  },
  {
    id: 'espiar-assinatura',
    fase: 2,
    titulo: 'Espiar uma assinatura',
    mostra: 'Cada assinatura é uma fila própria com a sua cópia das mensagens.',
    esperado:
      'Em alto-valor, que não tem serviço, as apólices de alto valor acumulam sozinhas. Nas outras, desligue a assinatura no seletor da coluna Cobrança ou Notificação e publique apólices.',
    campos: ['assinatura'],
    executar: (v) => espiar(`/assinaturas/${v.assinatura}/mensagens?dlq=false`, v.assinatura)(),
  },
  {
    id: 'dlq-assinatura',
    fase: 2,
    titulo: 'Espiar a DLQ de uma assinatura',
    mostra: 'Cada assinatura tem a sua própria dead-letter queue.',
    esperado: 'Normalmente vazia: os serviços só mandam para a DLQ quando o corpo não pode ser lido.',
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
      const regras = await chamar<unknown[]>('operacao', 'GET', '/assinaturas/regras');
      return `Regras lidas de ${plural(regras.length, 'assinatura', 'assinaturas')}. Veja a coluna da Operação.`;
    },
  },
  {
    id: 'endossos-em-ordem',
    fase: 3,
    titulo: 'Endossos em ordem (sessions)',
    mostra:
      'SessionId = apólice: as versões de cada apólice saem em ordem e uma de cada vez; apólices diferentes rodam em paralelo.',
    esperado:
      'Na Subscrição, cada apólice abre sua sessão e aplica v1, v2, v3, v4 nessa ordem. As apólices se intercalam entre si, mas nunca uma versão passa outra da mesma apólice.',
    campos: [],
    executar: async () => {
      const { apolices } = await chamar<{ apolices: string[] }>('subscricao', 'POST', '/demo/endossos/em-ordem', {
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
      'A Subscrição adia cada versão que chega antes da hora. Quando a que falta chega, aplica e retoma as adiadas em sequência, até a v5.',
    campos: [],
    executar: async () => {
      const { ordem } = await chamar<{ ordem: number[] }>('subscricao', 'POST', '/demo/endossos/fora-de-ordem');
      return `Enviados na ordem ${ordem.map((v) => `v${v}`).join(', ')}.`;
    },
  },
  {
    id: 'endosso-duplicado',
    fase: 3,
    titulo: 'Endosso duplicado (detecção de duplicatas)',
    mostra:
      'O producer reenvia a v1 com o mesmo MessageId. O broker aceita o envio, mas descarta a cópia: ela nunca chega ao consumer.',
    esperado: 'A Subscrição registra três envios aceitos e aplica só v1 e v2, uma vez cada.',
    campos: [],
    executar: async () => {
      const { apoliceId } = await chamar<{ apoliceId: string }>('subscricao', 'POST', '/demo/endossos/duplicado');
      return `Apólice ${apoliceId.slice(0, 8)}: 3 envios, 2 mensagens na fila.`;
    },
  },
  {
    id: 'falha-backoff',
    fase: 3,
    titulo: 'Falha transitória com backoff (agendamento)',
    mostra:
      'Em vez de Abandon (reentrega na hora), a Subscrição conclui e agenda uma cópia para daqui a 2 s, depois 4 s, na mesma transação. A dependência volta na 3ª tentativa.',
    esperado:
      'Duas falhas com espera crescente e aprovação na tentativa 3. Compare com "Falha transitória até a DLQ" da fase 1: lá as 5 entregas acabam em segundos.',
    campos: ['idade', 'capitalSegurado', 'plano'],
    executar: async (v) => {
      const proposta = await chamar<{ propostaId: string }>('propostas', 'POST', '/demo/propostas', {
        idade: numeroOuNulo(v.idade),
        capitalSegurado: numeroOuNulo(v.capitalSegurado),
        plano: textoOuNulo(v.plano),
        simularFalha: true,
        retryComBackoff: true,
      });
      return `Proposta ${proposta.propostaId.slice(0, 8)} enviada com falha simulada e retry agendado.`;
    },
  },
  {
    id: 'api-criar',
    fase: 4,
    titulo: 'Criar proposta pela API REST (outbox)',
    mostra:
      'POST /api/propostas grava a proposta e a mensagem do outbox numa única transação do SQL Server e responde 201 sem falar com o Service Bus. O despachante publica em seguida.',
    esperado:
      'Propostas: "gravada com a mensagem no outbox", depois "Outbox: mensagem publicada" com o atraso em ms. A Subscrição aprova como sempre, e Propostas recebe a apólice de volta ("Desfecho: ... aprovada"). O GET 3 s depois mostra Aprovada.',
    campos: ['idade', 'capitalSegurado', 'plano'],
    executar: async (v) => {
      const { status, cabecalhos, dados } = await chamarDetalhado<PropostaDaApi>(
        'propostas',
        'POST',
        '/api/propostas',
        novaProposta(v),
      );
      await esperar(3000);
      const depois = await consultarProposta(dados.propostaId);
      return `${status} Created, Location: ${cabecalhos.get('Location')}. Status logo após o POST: ${dados.status}; 3 s depois: ${descreverStatus(depois)}.`;
    },
  },
  {
    id: 'api-recusada',
    fase: 4,
    titulo: 'Proposta recusada pela API REST (desfecho de volta)',
    mostra:
      'A API aceita a proposta (201: dado válido) e quem recusa é a subscrição, depois. O evento PropostaRecusada volta para Propostas pela assinatura status-recusada, e o GET passa a mostrar a recusa.',
    esperado:
      'Subscrição: recusada por idade. Notificação: notificacao-recusa avisa o cliente. Propostas: "Desfecho: ... recusada". O GET 3 s depois mostra Recusada com o motivo.',
    campos: ['idade', 'capitalSegurado', 'plano'],
    padroes: { idade: '93' },
    executar: async (v) => {
      const { status, dados } = await chamarDetalhado<PropostaDaApi>('propostas', 'POST', '/api/propostas', novaProposta(v));
      await esperar(3000);
      const depois = await consultarProposta(dados.propostaId);
      return `POST: ${status}. 3 s depois: ${descreverStatus(depois)}.`;
    },
  },
  {
    id: 'api-idempotencia',
    fase: 4,
    titulo: 'POST repetido com Idempotency-Key',
    mostra:
      'O cliente não recebeu a resposta e repete o POST com a mesma chave. A API devolve a proposta já criada: nada é gravado nem publicado de novo.',
    esperado:
      'Primeiro POST: 201. Segundo: 200 com Idempotent-Replayed: true e o mesmo id. Só uma mensagem chega aa Subscrição.',
    campos: ['idade', 'capitalSegurado', 'plano'],
    executar: async (v) => {
      const chave = crypto.randomUUID();
      const corpo = novaProposta(v);
      const primeiro = await chamarDetalhado<PropostaDaApi>('propostas', 'POST', '/api/propostas', corpo, {
        'Idempotency-Key': chave,
      });
      const segundo = await chamarDetalhado<PropostaDaApi>('propostas', 'POST', '/api/propostas', corpo, {
        'Idempotency-Key': chave,
      });
      const mesmo = primeiro.dados.propostaId === segundo.dados.propostaId ? 'mesmo id' : 'ids DIFERENTES';
      return `1º POST: ${primeiro.status}. 2º POST: ${segundo.status} (Idempotent-Replayed: ${segundo.cabecalhos.get('Idempotent-Replayed') ?? 'ausente'}), ${mesmo}.`;
    },
  },
  {
    id: 'outbox-indisponivel',
    fase: 4,
    titulo: 'Service Bus fora do ar (outbox acumula)',
    mostra:
      'Simula o Service Bus indisponível só para o despachante. A API continua respondendo 201: o compromisso é com o banco, e o outbox guarda as mensagens.',
    esperado:
      'Três 201. Em Propostas, o despachante avisa que não publicou e reagenda com espera crescente. A Subscrição não recebe nada até "Restabelecer o Service Bus".',
    campos: [],
    executar: async (v) => {
      await chamar('propostas', 'POST', '/demo/outbox/simulacao', { indisponivel: true });
      const codigos: number[] = [];
      for (let i = 0; i < 3; i++) {
        codigos.push((await chamarDetalhado('propostas', 'POST', '/api/propostas', novaProposta(v))).status);
      }
      const { pendentes } = await chamar<{ pendentes: number }>('propostas', 'GET', '/demo/outbox');
      return `API respondeu ${codigos.join(', ')}. Outbox com ${plural(pendentes, 'mensagem pendente', 'mensagens pendentes')}.`;
    },
  },
  {
    id: 'outbox-restabelecer',
    fase: 4,
    titulo: 'Restabelecer o Service Bus',
    mostra: 'Desliga a simulação. O despachante publica o que acumulou, na ordem em que foi gravado.',
    esperado:
      'Em até 10 s (o maior backoff), Propostas registra as publicações com o atraso de cada uma, e a Subscrição aprova as propostas.',
    campos: [],
    executar: async () => {
      await chamar('propostas', 'POST', '/demo/outbox/simulacao', { indisponivel: false });
      const { pendentes } = await chamar<{ pendentes: number }>('propostas', 'GET', '/demo/outbox');
      return `Service Bus de volta. ${plural(pendentes, 'mensagem pendente', 'mensagens pendentes')} para publicar.`;
    },
  },
  {
    id: 'consumidor-desligado',
    fase: 4,
    titulo: 'Outbox com a Subscrição desligada',
    mostra:
      'Para o processor da fila subscricao-entrada sem derrubar o processo. A API e o outbox não dependem do consumidor: as propostas são aceitas, publicadas e esperam na fila.',
    esperado:
      'Cinco 201 e as propostas em EnviadaParaAnalise, com as mensagens paradas na fila. Religue "propostas" no seletor da coluna da Subscrição: ela drena a fila e o GET passa a mostrar Aprovada ou Recusada.',
    campos: [],
    executar: async (v) => {
      await chamar('subscricao', 'POST', '/processadores/propostas/parar');
      const ids: string[] = [];
      for (let i = 0; i < 5; i++) {
        const { dados } = await chamarDetalhado<PropostaDaApi>('propostas', 'POST', '/api/propostas', novaProposta(v));
        ids.push(dados.propostaId);
      }
      // Tempo para o despachante (polling de 1 s) publicar.
      await esperar(3000);
      const naFila = await chamar<unknown[]>('operacao', 'GET', '/filas/subscricao-entrada/mensagens?dlq=false');
      const consultas = ids.map(consultarProposta);
      const propostas = await Promise.all(consultas);
      const enviadas = propostas.filter((p) => p.status === 'EnviadaParaAnalise').length;
      return `Subscrição parada. 5 propostas aceitas; 3 s depois, ${enviadas} em EnviadaParaAnalise e ${plural(naFila.length, 'mensagem', 'mensagens')} esperando na fila subscricao-entrada. Religue "propostas" na coluna da Subscrição.`;
    },
  },
  {
    id: 'queda-apos-gravar',
    fase: 4,
    titulo: 'Queda entre gravar e concluir (inbox)',
    mostra:
      'A Subscrição grava a apólice no banco dela e "cai" antes de concluir a mensagem. O broker reentrega (at-least-once), e a inbox, com o MessageId já processado, impede a segunda apólice e o segundo evento.',
    esperado:
      'Na Subscrição: "gravada, mas o processo caiu antes de concluir", depois "já processada (MessageId na inbox, entrega 2)". Uma apólice só: Cobrança e Notificação recebem o evento uma vez.',
    campos: ['idade', 'capitalSegurado', 'plano'],
    executar: async (v) => {
      const proposta = await chamar<{ propostaId: string }>('propostas', 'POST', '/demo/propostas', {
        idade: numeroOuNulo(v.idade),
        capitalSegurado: numeroOuNulo(v.capitalSegurado),
        plano: textoOuNulo(v.plano),
        simularFalha: false,
        simularQuedaAposGravar: true,
      });
      return `Proposta ${proposta.propostaId.slice(0, 8)} enviada com a queda simulada depois da gravação.`;
    },
  },
  {
    id: 'dual-write',
    fase: 4,
    titulo: 'Sem outbox: dual write com falha',
    mostra:
      'O problema que o outbox resolve: grava no banco e publica no Service Bus em dois passos, sem transação entre eles. O processo "cai" entre os dois.',
    esperado:
      'Propostas registra a proposta gravada e nunca publicada. A Subscrição não recebe nada, e o GET mostra a proposta parada em Recebida.',
    campos: [],
    executar: async () => {
      const { propostaId } = await chamar<{ propostaId: string }>('propostas', 'POST', '/demo/propostas/sem-outbox', {
        falharAntesDoEnvio: true,
      });
      await esperar(2000);
      const depois = await consultarProposta(propostaId);
      return `Proposta ${propostaId.slice(0, 8)} gravada. 2 s depois: ${depois.status} (e vai continuar assim).`;
    },
  },
  {
    id: 'outbox-estado',
    fase: 4,
    titulo: 'Ver o outbox',
    mostra: 'Quantas mensagens o despachante ainda não publicou.',
    esperado: 'Com o Service Bus no ar, zero pendentes depois de alguns segundos.',
    campos: [],
    executar: async () => {
      const estado = await chamar<{ pendentes: number; publicadas: number; serviceBusIndisponivel: boolean }>(
        'propostas',
        'GET',
        '/demo/outbox',
      );
      return `${estado.pendentes} pendente(s), ${estado.publicadas} publicada(s). Service Bus ${estado.serviceBusIndisponivel ? 'fora do ar (simulação)' : 'no ar'}.`;
    },
  },
];
