/**
 * Os serviços .NET que o painel acompanha: uma coluna e uma rota no proxy do Vite (/api/<id>) para cada um.
 * Lista única: o tipo App, os nomes, as colunas e o vite.config.ts saem daqui. Um serviço novo entra aqui,
 * no array $apps do iniciar.ps1 e no .vscode/launch.json.
 */
export interface DescricaoDoApp {
  id: string;
  nome: string;
  /** Porta fixa (Urls no appsettings.json do serviço). */
  porta: number;
  /** Cor da coluna: um token de estilos.css, que já muda no tema escuro. */
  cor: string;
  /** O que o serviço faz, no cabeçalho da coluna. */
  papel: string;
  /** Pasta do projeto, para o comando que a coluna sugere quando o serviço está fora do ar. */
  pasta: string;
}

export const apps = [
  {
    id: 'propostas',
    nome: 'Propostas',
    porta: 5180,
    cor: 'var(--azul-apolice)',
    papel: 'API REST, outbox e desfechos',
    pasta: 'src\\Propostas\\Vida.Propostas',
  },
  {
    id: 'subscricao',
    nome: 'Subscrição',
    porta: 5181,
    cor: 'var(--verde-quitado)',
    papel: 'Analisa propostas, emite apólices, aplica endossos',
    pasta: 'src\\Subscricao\\Vida.Subscricao',
  },
  {
    id: 'cobranca',
    nome: 'Cobrança',
    porta: 5182,
    cor: 'var(--cobranca)',
    papel: 'Assinatura cobranca: boleto do 1º prêmio',
    pasta: 'src\\Cobranca\\Vida.Cobranca',
  },
  {
    id: 'notificacao',
    nome: 'Notificação',
    porta: 5183,
    cor: 'var(--notificacao)',
    papel: 'Boas-vindas e aviso de recusa',
    pasta: 'src\\Notificacao\\Vida.Notificacao',
  },
  {
    id: 'operacao',
    nome: 'Operação',
    porta: 5184,
    cor: 'var(--grafite-suave)',
    papel: 'Peek, DLQ e regras (descobre as entidades)',
    pasta: 'src\\Operacao\\Vida.Operacao',
  },
] as const satisfies readonly DescricaoDoApp[];

/** Id de um dos serviços acima. */
export type App = (typeof apps)[number]['id'];

const porId = new Map<string, DescricaoDoApp>(apps.map((app) => [app.id, app]));

/** Descrição completa de um app. */
export function descricaoDoApp(app: App): DescricaoDoApp {
  return porId.get(app)!;
}
