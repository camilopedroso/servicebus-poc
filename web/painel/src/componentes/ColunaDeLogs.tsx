import { useLayoutEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { separarAssinatura } from '../assinaturas';
import { linhaNaTrilha, partesDaMensagem, type Trilha } from '../correlacao';
import { enderecoDoAspire } from '../hooks/useAspire';
import { origemDaCategoria } from '../origens';
import type { App, EstadoDaConexao, LinhaDeLog } from '../tipos';

interface Props {
  app: App;
  titulo: string;
  papel: string;
  /** Cor do serviço (apps.ts), no filete do cabeçalho. */
  cor: string;
  comandoParaSubir: string;
  linhas: LinhaDeLog[];
  estado: EstadoDaConexao;
  onLimpar: () => void;
  trilha: Trilha | null;
  idFixado: string | null;
  onFixarId: (id: string) => void;
  /** Controles extras no cabeçalho (liga/desliga dos processors na Subscrição, das assinaturas na Cobrança e na Notificação). */
  controles?: ReactNode;
  /** Escondida pela barra de colunas (só no desktop; no celular quem escolhe são as abas). */
  oculta: boolean;
  ativaNoCelular: boolean;
  /** Se o Aspire Dashboard está no ar: o trace de cada linha abre nele; senão, o TraceId é copiado. */
  aspireNoAr: boolean;
}

const textoDoEstado: Record<EstadoDaConexao, string> = {
  conectando: 'Conectando',
  'ao-vivo': 'Ao vivo',
  reconectando: 'Fora do ar, tentando reconectar',
};

const formatoDaHora = new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });

export function ColunaDeLogs(props: Props) {
  const { linhas, estado, trilha } = props;
  const rolagem = useRef<HTMLDivElement>(null);
  const [seguindo, setSeguindo] = useState(true);

  // Mais recentes no topo. Enquanto o usuário estiver no topo, a coluna fica nele; se rolar para baixo
  // para ler as antigas, o navegador mantém a posição (scroll anchoring) mesmo com linhas novas entrando.
  useLayoutEffect(() => {
    const elemento = rolagem.current;
    if (elemento && seguindo) elemento.scrollTop = 0;
  }, [linhas, seguindo]);

  const aoRolar = () => {
    const elemento = rolagem.current;
    if (!elemento) return;
    setSeguindo(elemento.scrollTop < 24);
  };

  const maisRecentesPrimeiro = [...linhas].reverse();

  const naTrilha = trilha ? linhas.filter((l) => linhaNaTrilha(l, trilha)).length : 0;

  return (
    <section
      className="coluna"
      aria-labelledby={`coluna-${props.app}`}
      data-ativa-no-celular={props.ativaNoCelular}
      data-oculta={props.oculta}
      style={{ '--cor-do-app': props.cor } as CSSProperties}
      id={`painel-${props.app}`}
      role="tabpanel"
    >
      <header className="coluna__cabecalho">
        <div className="coluna__identidade">
          <h2 className="coluna__titulo" id={`coluna-${props.app}`}>
            <span className={`conexao conexao--${estado}`} title={textoDoEstado[estado]} />
            {props.titulo}
            <span className="visualmente-oculto">, {textoDoEstado[estado]}</span>
          </h2>
          <p className="coluna__papel">{props.papel}</p>
        </div>
        <div className="coluna__acoes">
          {props.controles}
          <button className="botao-discreto" onClick={props.onLimpar} disabled={linhas.length === 0}>
            Limpar
          </button>
        </div>
      </header>

      {estado === 'reconectando' && linhas.length > 0 && (
        <p className="coluna__aviso">
          Fora do ar. Para subir: <code>{props.comandoParaSubir}</code>
        </p>
      )}

      {trilha && (
        <p className="coluna__contagem-da-trilha">
          {naTrilha === 0 ? 'Nenhuma linha desta trilha aqui' : `${naTrilha} ${naTrilha === 1 ? 'linha' : 'linhas'} desta trilha`}
        </p>
      )}

      <div
        className="coluna__linhas"
        ref={rolagem}
        onScroll={aoRolar}
        role="log"
        aria-live="polite"
        aria-relevant="additions"
        tabIndex={0}
      >
        {linhas.length === 0 ? (
          <Vazia estado={estado} titulo={props.titulo} comando={props.comandoParaSubir} />
        ) : (
          <ol className="linhas">
            {maisRecentesPrimeiro.map((linha) => {
              if (linha.tipo === 'reinicio') {
                // Lida de cima para baixo: acima ficam as linhas do processo novo, abaixo as do anterior.
                return (
                  <li key={linha.chave} className="linha-de-reinicio">
                    {props.titulo} reiniciado às {formatoDaHora.format(new Date(linha.momento))}
                  </li>
                );
              }
              const { assinatura, resto } = separarAssinatura(linha.registro.mensagem);
              // O selo de assinatura já diz de onde a linha veio; os demais componentes saem da categoria do logger.
              const origem = assinatura ? null : origemDaCategoria(linha.registro.categoria);
              return (
                <li
                  key={linha.chave}
                  className="linha"
                  data-nivel={linha.registro.nivel}
                  data-assinatura={assinatura ?? undefined}
                  data-trilha={trilha ? (linhaNaTrilha(linha, trilha) ? 'dentro' : 'fora') : undefined}
                >
                  <time className="linha__hora" dateTime={linha.registro.momento}>
                    {formatoDaHora.format(new Date(linha.registro.momento))}
                  </time>
                  <span className="linha__mensagem">
                    {assinatura && (
                      <span className="selo" data-assinatura={assinatura}>
                        {assinatura}
                      </span>
                    )}
                    {origem && (
                      <span className="selo selo--componente" title={origem.descricao}>
                        {origem.rotulo}
                      </span>
                    )}
                    {partesDaMensagem(resto, linha.registro.ids).map((parte, i) =>
                      parte.tipo === 'texto' ? (
                        parte.texto
                      ) : (
                        <button
                          key={i}
                          className="id"
                          data-fixado={parte.id === props.idFixado}
                          onClick={() => props.onFixarId(parte.id)}
                          title={`${parte.id}\nAcompanhar nas três colunas`}
                          aria-label={`Acompanhar ${parte.id} nas três colunas`}
                        >
                          {parte.id.slice(0, 8)}
                        </button>
                      ),
                    )}
                    {linha.registro.traceId && (
                      <ControleDeTrace traceId={linha.registro.traceId} aspireNoAr={props.aspireNoAr} />
                    )}
                    {linha.registro.excecao && <span className="linha__excecao">{linha.registro.excecao}</span>}
                  </span>
                </li>
              );
            })}
          </ol>
        )}
      </div>

      {!seguindo && linhas.length > 0 && (
        <button className="voltar-ao-topo" onClick={() => setSeguindo(true)}>
          Ver mais recentes
        </button>
      )}
    </section>
  );
}

/**
 * Fase 5: do log ao trace. Com o Aspire Dashboard no ar, abre o trace nele (aparece na hora). Sem ele, copia o
 * TraceId para colar na busca do Application Insights (Transaction search), onde o trace chega em alguns minutos.
 */
function ControleDeTrace({ traceId, aspireNoAr }: { traceId: string; aspireNoAr: boolean }) {
  const [copiado, setCopiado] = useState(false);

  if (aspireNoAr) {
    return (
      <a
        className="linha__trace"
        href={`${enderecoDoAspire}/traces/detail/${traceId}`}
        target="_blank"
        rel="noreferrer"
        title={`Abrir o trace ${traceId} no Aspire Dashboard`}
      >
        trace ↗
      </a>
    );
  }

  const copiar = async () => {
    try {
      await navigator.clipboard.writeText(traceId);
      setCopiado(true);
      setTimeout(() => setCopiado(false), 1500);
    } catch {
      // Sem permissão de área de transferência: o TraceId continua visível no tooltip.
    }
  };
  return (
    <button
      className="linha__trace"
      onClick={copiar}
      title={`TraceId ${traceId}\nCopia para buscar no Application Insights (Transaction search).\nPara abrir na hora: iniciar.ps1 -ComAspire`}
    >
      {copiado ? 'copiado' : 'trace'}
    </button>
  );
}

function Vazia({ estado, titulo, comando }: { estado: EstadoDaConexao; titulo: string; comando: string }) {
  if (estado === 'reconectando') {
    return (
      <div className="vazia">
        <p>O {titulo} não está respondendo. Suba ele em outro terminal:</p>
        <code className="vazia__comando">{comando}</code>
        <p>A coluna reconecta sozinha.</p>
      </div>
    );
  }
  if (estado === 'conectando') {
    return <p className="vazia">Conectando ao {titulo}…</p>;
  }
  return <p className="vazia">Nada por aqui ainda. Execute um cenário para ver as mensagens chegarem.</p>;
}
