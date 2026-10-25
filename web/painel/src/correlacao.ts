import type { LinhaDeLog } from './tipos';

/**
 * A partir de um id fixado, junta todos os ids que aparecem na mesma linha que ele, nas três colunas.
 * Assim, fixar uma PropostaId também acende a ApoliceId que o Consumer emitiu para ela,
 * e as linhas dos assinantes (que só conhecem a ApoliceId) entram na trilha.
 */
export function idsDaTrilha(fixado: string, todasAsLinhas: LinhaDeLog[]): Set<string> {
  const trilha = new Set([fixado]);
  // Proposta → apólice → (nada além) cabe em duas voltas; a terceira garante o ponto fixo.
  for (let volta = 0; volta < 3; volta++) {
    const tamanhoAntes = trilha.size;
    for (const linha of todasAsLinhas) {
      if (linha.tipo !== 'registro') continue;
      const ids = Object.values(linha.registro.ids);
      if (ids.some((id) => trilha.has(id))) {
        ids.forEach((id) => trilha.add(id));
      }
    }
    if (trilha.size === tamanhoAntes) break;
  }
  return trilha;
}

/** Ids de negócio ligados ao id fixado, e os traces em que eles aparecem. */
export interface Trilha {
  ids: Set<string>;
  traces: Set<string>;
}

/**
 * Monta a trilha: primeiro pelos ids (idsDaTrilha), depois junta os traces das linhas que entraram.
 * Os traces servem para trazer linhas que não têm id de negócio, como "Lote enviado com N mensagens".
 */
export function montarTrilha(fixado: string, todasAsLinhas: LinhaDeLog[]): Trilha {
  const ids = idsDaTrilha(fixado, todasAsLinhas);
  const traces = new Set<string>();
  for (const linha of todasAsLinhas) {
    if (linha.tipo === 'registro' && linha.registro.traceId && temIdNaTrilha(linha, ids)) {
      traces.add(linha.registro.traceId);
    }
  }
  return { ids, traces };
}

/**
 * Linha com id de negócio: entra pelos ids. Linha sem id: entra pelo trace.
 * O trace não puxa linhas que têm ids, de propósito: um trace pode carregar várias mensagens (o POST de um lote,
 * um lote do despachante do outbox), e a trilha acenderia o lote inteiro em vez da mensagem fixada.
 */
export function linhaNaTrilha(linha: LinhaDeLog, trilha: Trilha): boolean {
  if (linha.tipo !== 'registro') return false;
  if (Object.keys(linha.registro.ids).length > 0) return temIdNaTrilha(linha, trilha.ids);
  return !!linha.registro.traceId && trilha.traces.has(linha.registro.traceId);
}

function temIdNaTrilha(linha: LinhaDeLog, ids: Set<string>): boolean {
  return linha.tipo === 'registro' && Object.values(linha.registro.ids).some((id) => ids.has(id));
}

export type ParteDaMensagem = { tipo: 'texto'; texto: string } | { tipo: 'id'; id: string };

/** Quebra a mensagem em texto e ids, para os ids virarem botões. */
export function partesDaMensagem(mensagem: string, ids: Record<string, string>): ParteDaMensagem[] {
  const valores = [...new Set(Object.values(ids))].filter((v) => mensagem.includes(v));
  if (valores.length === 0) return [{ tipo: 'texto', texto: mensagem }];

  const padrao = new RegExp(`(${valores.map((v) => v.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')})`);
  return mensagem
    .split(padrao)
    .filter((pedaco) => pedaco !== '')
    .map((pedaco) => (valores.includes(pedaco) ? { tipo: 'id', id: pedaco } : { tipo: 'texto', texto: pedaco }));
}
