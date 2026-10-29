/** Assinaturas dos tópicos apolice-emitida e proposta-recusada, na ordem de Vida.Subscricao.Contratos.Topologia.Assinaturas. */
export const assinaturas = [
  'cobranca',
  'notificacao',
  'alto-valor',
  'notificacao-recusa',
  'status-aprovada',
  'status-recusada',
] as const;

export type Assinatura = (typeof assinaturas)[number];

const prefixo = new RegExp(`^\\[(${assinaturas.join('|')})\\]\\s*`);

/**
 * O ProcessadorDeAssinatura começa todo log com "[assinatura]". O painel troca esse prefixo por um selo colorido.
 * @returns a assinatura (ou null, se a linha não for de uma) e a mensagem sem o prefixo.
 */
export function separarAssinatura(mensagem: string): { assinatura: Assinatura | null; resto: string } {
  const encontrado = prefixo.exec(mensagem);
  return encontrado
    ? { assinatura: encontrado[1] as Assinatura, resto: mensagem.slice(encontrado[0].length) }
    : { assinatura: null, resto: mensagem };
}
