import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { chamar, ErroDaApi } from '../api';
import { useFecharAoSair } from '../hooks/useFecharAoSair';
import type { App, EstadoDoProcessador } from '../tipos';

interface Props {
  /** App cujos processors o controle liga e desliga (GET /processadores). */
  app: App;
  /** Muda depois de cada cenário executado: um cenário pode ter ligado ou desligado um processor. */
  versao: number;
}

const INTERVALO_COM_LISTA_ABERTA = 3_000;
const INTERVALO_COM_LISTA_FECHADA = 15_000;

/**
 * Liga e desliga os processors de um app em tempo de execução (ProcessadoresController).
 * Desligado, o processo continua no ar e as mensagens esperam na fila: dá para ver o outbox publicando sem consumidor.
 * Usa o mesmo visual do seletor de assinaturas.
 */
export function ControleDeProcessadores({ app, versao }: Props) {
  const [aberto, setAberto] = useState(false);
  const [estados, setEstados] = useState<EstadoDoProcessador[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [alterando, setAlterando] = useState<string | null>(null);
  const raiz = useRef<HTMLDivElement>(null);
  const idDaLista = useId();
  const fechar = useCallback(() => setAberto(false), []);
  useFecharAoSair(aberto, raiz, fechar);

  const atualizar = useCallback(async () => {
    try {
      setEstados(await chamar<EstadoDoProcessador[]>(app, 'GET', '/processadores'));
      setErro(null);
    } catch (e) {
      setErro(e instanceof ErroDaApi ? e.message : 'Não foi possível ler os processadores.');
    }
  }, [app]);

  useEffect(() => {
    void atualizar();
    const intervalo = setInterval(atualizar, aberto ? INTERVALO_COM_LISTA_ABERTA : INTERVALO_COM_LISTA_FECHADA);
    return () => clearInterval(intervalo);
  }, [aberto, versao, atualizar]);

  const alternar = async (processador: EstadoDoProcessador) => {
    setAlterando(processador.nome);
    try {
      await chamar(app, 'POST', `/processadores/${processador.nome}/${processador.ativo ? 'parar' : 'iniciar'}`);
      await atualizar();
    } catch (e) {
      setErro(e instanceof ErroDaApi ? e.message : 'Não foi possível alterar o processador.');
    } finally {
      setAlterando(null);
    }
  };

  const ligados = estados?.filter((e) => e.ativo).length;
  const rotulo =
    estados === null
      ? erro
        ? 'Processadores indisponíveis'
        : 'Processadores'
      : `Processadores ligados: ${ligados} de ${estados.length}`;

  return (
    <div className="assinaturas" ref={raiz}>
      <button
        className="assinaturas__gatilho"
        aria-expanded={aberto}
        aria-controls={idDaLista}
        onClick={() => setAberto((a) => !a)}
      >
        {rotulo}
        <svg className="assinaturas__seta" viewBox="0 0 10 6" aria-hidden="true">
          <path d="M1 1l4 4 4-4" fill="none" stroke="currentColor" strokeWidth="1.5" />
        </svg>
      </button>

      {aberto && (
        <div className="assinaturas__lista" id={idDaLista}>
          {erro && <p className="assinaturas__erro">{erro}</p>}
          {estados?.map((processador) => (
            <div key={processador.nome} className="assinatura" data-ativa={processador.ativo}>
              <button
                role="switch"
                aria-checked={processador.ativo}
                className="interruptor"
                disabled={alterando !== null}
                onClick={() => alternar(processador)}
                aria-label={`${processador.ativo ? 'Desligar' : 'Ligar'} ${processador.nome}`}
              >
                <span className="interruptor__trilho" aria-hidden="true">
                  <span className="interruptor__pino" />
                </span>
              </button>
              <div className="assinatura__texto">
                <span className="assinatura__nome">{processador.nome}</span>
                <span className="assinatura__filtro">{processador.entidade}</span>
              </div>
              <span className="assinatura__pendentes">{processador.ativo ? 'ligado' : 'parado'}</span>
            </div>
          ))}
          <p className="assinaturas__nota">
            Parado, o processo continua no ar e as mensagens esperam na fila até ele ser religado.
          </p>
        </div>
      )}
    </div>
  );
}
