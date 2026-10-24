import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { chamar, ErroDaApi } from '../api';
import type { EstadoDaAssinatura } from '../tipos';

interface Props {
  /** Muda depois de cada cenário executado, para atualizar as contagens. */
  versao: number;
}

const INTERVALO_COM_LISTA_ABERTA = 3_000;
const INTERVALO_COM_LISTA_FECHADA = 15_000;

/**
 * Liga e desliga as assinaturas do Vida.Assinantes em tempo de execução.
 * Uma assinatura desligada continua recebendo cópias do tópico: o número de pendentes sobe até ela ser religada.
 */
export function ControleDeAssinaturas({ versao }: Props) {
  const [aberto, setAberto] = useState(false);
  const [estados, setEstados] = useState<EstadoDaAssinatura[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [alterando, setAlterando] = useState<string | null>(null);
  const raiz = useRef<HTMLDivElement>(null);
  const idDaLista = useId();

  const atualizar = useCallback(async () => {
    try {
      setEstados(await chamar<EstadoDaAssinatura[]>('assinantes', 'GET', '/assinaturas'));
      setErro(null);
    } catch (e) {
      setErro(e instanceof ErroDaApi ? e.message : 'Não foi possível ler as assinaturas.');
    }
  }, []);

  // Contagens mudam sozinhas (mensagens chegando): atualiza mais rápido enquanto a lista está aberta.
  useEffect(() => {
    void atualizar();
    const intervalo = setInterval(atualizar, aberto ? INTERVALO_COM_LISTA_ABERTA : INTERVALO_COM_LISTA_FECHADA);
    return () => clearInterval(intervalo);
  }, [aberto, versao, atualizar]);

  // Fecha com Esc ou com clique fora.
  useEffect(() => {
    if (!aberto) return;
    const aoTeclar = (e: KeyboardEvent) => e.key === 'Escape' && setAberto(false);
    const aoClicar = (e: PointerEvent) => {
      if (raiz.current && !raiz.current.contains(e.target as Node)) setAberto(false);
    };
    document.addEventListener('keydown', aoTeclar);
    document.addEventListener('pointerdown', aoClicar);
    return () => {
      document.removeEventListener('keydown', aoTeclar);
      document.removeEventListener('pointerdown', aoClicar);
    };
  }, [aberto]);

  const alternar = async (assinatura: EstadoDaAssinatura) => {
    setAlterando(assinatura.nome);
    try {
      await chamar('assinantes', 'POST', `/assinaturas/${assinatura.nome}/${assinatura.ativa ? 'parar' : 'iniciar'}`);
      await atualizar();
    } catch (e) {
      setErro(e instanceof ErroDaApi ? e.message : 'Não foi possível alterar a assinatura.');
    } finally {
      setAlterando(null);
    }
  };

  const ligadas = estados?.filter((e) => e.ativa).length;
  const rotulo =
    estados === null ? (erro ? 'Assinaturas indisponíveis' : 'Assinaturas') : `Assinaturas ligadas: ${ligadas} de ${estados.length}`;

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
          {estados?.map((assinatura) => (
            <div key={assinatura.nome} className="assinatura" data-ativa={assinatura.ativa}>
              <button
                role="switch"
                aria-checked={assinatura.ativa}
                className="interruptor"
                disabled={alterando !== null}
                onClick={() => alternar(assinatura)}
                aria-label={`${assinatura.ativa ? 'Desligar' : 'Ligar'} ${assinatura.nome}`}
              >
                <span className="interruptor__trilho" aria-hidden="true">
                  <span className="interruptor__pino" />
                </span>
              </button>
              <div className="assinatura__texto">
                {/* Mesmo selo das linhas de log: a lista serve de legenda das cores. */}
                <span className="assinatura__nome">
                  <span className="selo" data-assinatura={assinatura.nome}>
                    {assinatura.nome}
                  </span>
                </span>
                {assinatura.filtros.map((filtro) => (
                  <span key={filtro} className="assinatura__filtro">
                    {filtro}
                  </span>
                ))}
              </div>
              <span className="assinatura__pendentes" data-acumulando={!assinatura.ativa && assinatura.pendentes > 0}>
                {assinatura.pendentes} {assinatura.pendentes === 1 ? 'pendente' : 'pendentes'}
                {assinatura.naDlq > 0 && <span className="assinatura__dlq">{assinatura.naDlq} na DLQ</span>}
              </span>
            </div>
          ))}
          <p className="assinaturas__nota">
            Desligada, a assinatura continua recebendo cópias do tópico e as guarda até ser religada.
          </p>
        </div>
      )}
    </div>
  );
}
