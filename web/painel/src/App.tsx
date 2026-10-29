import { useEffect, useMemo, useState, type CSSProperties, type FormEvent } from 'react';
import { chamar, ErroDaApi } from './api';
import { apps, type App as NomeDoApp } from './apps';
import { cenarios, valoresIniciais, type Cenario, type Valores } from './cenarios';
import { CartaoDoResseguro } from './componentes/CartaoDoResseguro';
import { ColunaDeLogs } from './componentes/ColunaDeLogs';
import { CamposDoCenario } from './componentes/CamposDoCenario';
import { ControleDeAssinaturas } from './componentes/ControleDeAssinaturas';
import { ControleDeProcessadores } from './componentes/ControleDeProcessadores';
import { SeletorDeCenario } from './componentes/SeletorDeCenario';
import { montarTrilha } from './correlacao';
import { useAspire } from './hooks/useAspire';
import { useColunasOcultas } from './hooks/useColunasOcultas';
import { useLogsAoVivo } from './hooks/useLogsAoVivo';

type Resultado = { tipo: 'executando' } | { tipo: 'ok'; texto: string } | { tipo: 'erro'; texto: string } | null;

type LogsDoApp = ReturnType<typeof useLogsAoVivo>;

/** Liga/desliga no cabeçalho da coluna: processors na Subscrição, assinaturas (com filtros e contagens) nos que assinam tópicos. */
function controlesDaColuna(app: NomeDoApp, versao: number) {
  if (app === 'subscricao') return <ControleDeProcessadores app="subscricao" versao={versao} />;
  if (app === 'cobranca' || app === 'notificacao') return <ControleDeAssinaturas app={app} versao={versao} />;
  return undefined;
}

export function App() {
  const [cenario, setCenario] = useState<Cenario>(cenarios[0]);
  const [valores, setValores] = useState<Valores>(valoresIniciais);
  const [resultado, setResultado] = useState<Resultado>(null);
  const [versao, setVersao] = useState(0);
  const [idFixado, setIdFixado] = useState<string | null>(null);
  const [colunaNoCelular, setColunaNoCelular] = useState<NomeDoApp>(apps[0].id);
  const [namespace, setNamespace] = useState<string | null>(null);
  const aspireNoAr = useAspire();
  const { ocultas, alternar } = useColunasOcultas();

  // Um stream de logs por app. A lista é constante (apps.ts), então os hooks são chamados sempre na mesma ordem.
  const logs = {} as Record<NomeDoApp, LogsDoApp>;
  for (const app of apps) {
    logs[app.id] = useLogsAoVivo(app.id);
  }

  useEffect(() => {
    chamar<{ namespace: string }>('operacao', 'GET', '/namespace')
      .then((r) => setNamespace(r.namespace))
      .catch(() => setNamespace(null));
  }, [logs.operacao.estado]);

  // A trilha olha todas as colunas, inclusive as escondidas: esconder é só visual.
  const linhasPorApp = apps.map((app) => logs[app.id].linhas);
  const trilha = useMemo(
    () => (idFixado ? montarTrilha(idFixado, linhasPorApp.flat()) : null),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- uma dependência por app, em número fixo
    [idFixado, ...linhasPorApp],
  );

  const escolher = (novo: Cenario) => {
    setCenario(novo);
    setValores({ ...valoresIniciais, ...novo.padroes });
    setResultado(null);
  };

  const executar = async (e: FormEvent) => {
    e.preventDefault();
    setResultado({ tipo: 'executando' });
    try {
      setResultado({ tipo: 'ok', texto: await cenario.executar(valores) });
    } catch (erro) {
      setResultado({ tipo: 'erro', texto: erro instanceof ErroDaApi ? erro.message : String(erro) });
    } finally {
      setVersao((v) => v + 1);
    }
  };

  const fixar = (id: string) => setIdFixado((atual) => (atual === id ? null : id));

  const visiveis = apps.filter((app) => !ocultas.has(app.id));
  const estiloDasColunas = { '--quantidade-de-colunas': visiveis.length } as CSSProperties;

  return (
    <div className="painel">
      <header className="topo">
        <div>
          <h1 className="topo__titulo">Laboratório Service Bus</h1>
          <p className="topo__subtitulo">Emissão de apólices do portfólio de Vida, um serviço por squad, conversando por eventos</p>
        </div>
        <p className="topo__namespace" title="Namespace do Service Bus">
          {namespace ?? 'Namespace desconhecido até a Operação responder'}
        </p>
      </header>

      <form className="controles" onSubmit={executar}>
        <div className="controles__linha">
          <SeletorDeCenario cenario={cenario} onEscolher={escolher} />
          <CamposDoCenario campos={cenario.campos} valores={valores} onAlterar={setValores} />
          <button className="botao-principal" type="submit" disabled={resultado?.tipo === 'executando'}>
            {resultado?.tipo === 'executando' ? 'Executando…' : 'Executar cenário'}
          </button>
        </div>
        <div className="controles__explicacao">
          <p>
            <strong>O que mostra.</strong> {cenario.mostra}
          </p>
          <p>
            <strong>O que esperar.</strong> {cenario.esperado}
          </p>
        </div>
        <p className="resultado" data-tipo={resultado?.tipo} role="status">
          {resultado?.tipo === 'ok' && <>Cenário executado. {resultado.texto}</>}
          {resultado?.tipo === 'erro' && resultado.texto}
          {resultado?.tipo === 'executando' &&
            'A primeira chamada pode levar uns 20 s enquanto o DefaultAzureCredential encontra o login do az.'}
        </p>
      </form>

      <div className="trilha" data-visivel={trilha !== null}>
        {idFixado ? (
          <>
            <p className="trilha__texto">
              Acompanhando <code>{idFixado}</code>
              {trilha && trilha.ids.size > 1 && (
                <>
                  {' '}
                  e mais {trilha.ids.size - 1} {trilha.ids.size === 2 ? 'id ligado' : 'ids ligados'} a ele
                </>
              )}
            </p>
            <button className="botao-discreto" onClick={() => setIdFixado(null)}>
              Parar de acompanhar
            </button>
          </>
        ) : (
          <p className="trilha__dica">Clique em um id em qualquer coluna para seguir a mensagem por todos os serviços.</p>
        )}
      </div>

      <div className="servicos">
        <div className="servicos__colunas" role="group" aria-label="Colunas visíveis">
          <span className="servicos__rotulo">Colunas</span>
          {apps.map((app) => {
            const visivel = !ocultas.has(app.id);
            return (
              <button
                key={app.id}
                className="chip-de-coluna"
                aria-pressed={visivel}
                style={{ '--cor-do-app': app.cor } as CSSProperties}
                // A última visível não esconde: alguma coluna sempre fica na tela.
                disabled={visivel && visiveis.length === 1}
                onClick={() => alternar(app.id)}
                title={visivel ? `Esconder a coluna ${app.nome} (os logs continuam chegando)` : `Mostrar a coluna ${app.nome}`}
              >
                {app.nome}
              </button>
            );
          })}
        </div>
        <CartaoDoResseguro versao={versao} />
      </div>

      <div className="abas" role="tablist" aria-label="Serviço">
        {apps.map((app) => (
          <button
            key={app.id}
            role="tab"
            aria-selected={colunaNoCelular === app.id}
            aria-controls={`painel-${app.id}`}
            className="aba"
            onClick={() => setColunaNoCelular(app.id)}
          >
            {app.nome}
          </button>
        ))}
      </div>

      <main className="colunas" style={estiloDasColunas}>
        {apps.map((app) => (
          <ColunaDeLogs
            key={app.id}
            app={app.id}
            titulo={app.nome}
            papel={app.papel}
            cor={app.cor}
            comandoParaSubir={`dotnet run --project ${app.pasta}`}
            linhas={logs[app.id].linhas}
            estado={logs[app.id].estado}
            onLimpar={logs[app.id].limpar}
            trilha={trilha}
            idFixado={idFixado}
            onFixarId={fixar}
            controles={controlesDaColuna(app.id, versao)}
            oculta={ocultas.has(app.id)}
            ativaNoCelular={colunaNoCelular === app.id}
            aspireNoAr={aspireNoAr}
          />
        ))}
      </main>
    </div>
  );
}
