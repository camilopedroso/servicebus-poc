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

export function linhaNaTrilha(linha: LinhaDeLog, trilha: Set<string>): boolean {
  return linha.tipo === 'registro' && Object.values(linha.registro.ids).some((id) => trilha.has(id));
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
