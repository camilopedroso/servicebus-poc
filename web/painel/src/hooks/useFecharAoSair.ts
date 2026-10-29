import { useEffect, type RefObject } from 'react';

/** Fecha um menu suspenso com Esc ou com clique fora dele (seletores de assinaturas e de processadores). */
export function useFecharAoSair(aberto: boolean, raiz: RefObject<HTMLElement | null>, fechar: () => void) {
  useEffect(() => {
    if (!aberto) return;
    const aoTeclar = (e: KeyboardEvent) => e.key === 'Escape' && fechar();
    const aoClicar = (e: PointerEvent) => {
      if (raiz.current && !raiz.current.contains(e.target as Node)) fechar();
    };
    document.addEventListener('keydown', aoTeclar);
    document.addEventListener('pointerdown', aoClicar);
    return () => {
      document.removeEventListener('keydown', aoTeclar);
      document.removeEventListener('pointerdown', aoClicar);
    };
  }, [aberto, raiz, fechar]);
}
