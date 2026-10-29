import { useEffect, useState } from 'react';
import { chamar } from '../api';

interface AssinaturaDescoberta {
  topico: string;
  nome: string;
  ativas: number;
  naDlq: number;
}

interface Props {
  /** Muda depois de cada cenário executado, para atualizar a contagem na hora. */
  versao: number;
}

const INTERVALO = 5_000;

/**
 * A squad de Resseguro ainda não tem serviço, então não tem coluna. A assinatura dela (alto-valor) existe no Bicep
 * e recebe as apólices em revisão: elas esperam lá. A contagem vem da Operação, que descobre as assinaturas no broker.
 */
export function CartaoDoResseguro({ versao }: Props) {
  const [assinatura, setAssinatura] = useState<AssinaturaDescoberta | null>(null);
  const [indisponivel, setIndisponivel] = useState(false);

  useEffect(() => {
    const atualizar = async () => {
      try {
        const assinaturas = await chamar<AssinaturaDescoberta[]>('operacao', 'GET', '/assinaturas');
        const altoValor = assinaturas.find((a) => a.topico === 'apolice-emitida' && a.nome === 'alto-valor') ?? null;
        setAssinatura(altoValor);
        setIndisponivel(false);
      } catch {
        setIndisponivel(true);
      }
    };
    void atualizar();
    const intervalo = setInterval(atualizar, INTERVALO);
    return () => clearInterval(intervalo);
  }, [versao]);

  const pendentes = assinatura?.ativas ?? 0;
  const contagem = indisponivel
    ? 'contagem indisponível sem a Operação'
    : assinatura === null
      ? 'assinatura alto-valor não encontrada'
      : `${pendentes} ${pendentes === 1 ? 'apólice esperando' : 'apólices esperando'} em alto-valor`;

  return (
    <div
      className="resseguro"
      data-acumulando={pendentes > 0}
      title="A squad de Resseguro ainda não tem serviço. A assinatura alto-valor existe e recebe as apólices em revisão: elas esperam lá (TTL de 14 dias, depois DLQ). Quem publica não sabe nem precisa saber. Veja com o cenário &quot;Espiar uma assinatura&quot;."
    >
      <span className="resseguro__nome">Resseguro</span>
      <span className="resseguro__situacao">sem serviço</span>
      <span className="resseguro__contagem">
        {contagem}
        {assinatura !== null && assinatura.naDlq > 0 && <span className="resseguro__dlq"> · {assinatura.naDlq} na DLQ</span>}
      </span>
    </div>
  );
}
