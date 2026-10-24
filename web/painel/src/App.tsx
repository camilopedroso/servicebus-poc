import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { chamar, ErroDaApi, nomeDoApp } from './api';
import { cenarios, valoresIniciais, type Cenario, type Valores } from './cenarios';
import { ColunaDeLogs } from './componentes/ColunaDeLogs';
import { CamposDoCenario } from './componentes/CamposDoCenario';
import { ControleDeAssinaturas } from './componentes/ControleDeAssinaturas';
import { SeletorDeCenario } from './componentes/SeletorDeCenario';
import { idsDaTrilha } from './correlacao';
import { useLogsAoVivo } from './hooks/useLogsAoVivo';
import type { App as NomeDoApp } from './tipos';

type Resultado = { tipo: 'executando' } | { tipo: 'ok'; texto: string } | { tipo: 'erro'; texto: string } | null;

const colunas: { app: NomeDoApp; papel: string; comando: string }[] = [
  { app: 'producer', papel: 'Envia para a fila e para o tópico', comando: 'dotnet run --project src\\Vida.Producer' },
  { app: 'consumer', papel: 'Fila propostas, publica no tópico', comando: 'dotnet run --project src\\Vida.Consumer' },
  { app: 'assinantes', papel: 'Assinaturas do tópico', comando: 'dotnet run --project src\\Vida.Assinantes' },
];

export function App() {
  const [cenario, setCenario] = useState<Cenario>(cenarios[0]);
  const [valores, setValores] = useState<Valores>(valoresIniciais);
  const [resultado, setResultado] = useState<Resultado>(null);
  const [versao, setVersao] = useState(0);
  const [idFixado, setIdFixado] = useState<string | null>(null);
  const [colunaNoCelular, setColunaNoCelular] = useState<NomeDoApp>('producer');
  const [namespace, setNamespace] = useState<string | null>(null);

  const logs = {
    producer: useLogsAoVivo('producer'),
    consumer: useLogsAoVivo('consumer'),
    assinantes: useLogsAoVivo('assinantes'),
  };

  useEffect(() => {
    chamar<{ namespace: string }>('producer', 'GET', '/namespace')
      .then((r) => setNamespace(r.namespace))
      .catch(() => setNamespace(null));
  }, [logs.producer.estado]);

  const [linhasProducer, linhasConsumer, linhasAssinantes] = [
    logs.producer.linhas,
    logs.consumer.linhas,
    logs.assinantes.linhas,
  ];
  const trilha = useMemo(
    () => (idFixado ? idsDaTrilha(idFixado, [...linhasProducer, ...linhasConsumer, ...linhasAssinantes]) : null),
    [idFixado, linhasProducer, linhasConsumer, linhasAssinantes],
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

  return (
    <div className="painel">
      <header className="topo">
        <div>
          <h1 className="topo__titulo">Laboratório Service Bus</h1>
          <p className="topo__subtitulo">Emissão de apólices do portfólio de Vida, do envio da proposta às assinaturas</p>
        </div>
        <p className="topo__namespace" title="Namespace do Service Bus">
          {namespace ?? 'Namespace desconhecido até o Producer responder'}
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
              {trilha && trilha.size > 1 && (
                <>
                  {' '}
                  e mais {trilha.size - 1} {trilha.size === 2 ? 'id ligado' : 'ids ligados'} a ele
                </>
              )}
            </p>
            <button className="botao-discreto" onClick={() => setIdFixado(null)}>
              Parar de acompanhar
            </button>
          </>
        ) : (
          <p className="trilha__dica">Clique em um id em qualquer coluna para seguir a mensagem pelos três processos.</p>
        )}
      </div>

      <div className="abas" role="tablist" aria-label="Processo">
        {colunas.map(({ app }) => (
          <button
            key={app}
            role="tab"
            aria-selected={colunaNoCelular === app}
            aria-controls={`painel-${app}`}
            className="aba"
            onClick={() => setColunaNoCelular(app)}
          >
            {nomeDoApp[app]}
          </button>
        ))}
      </div>

      <main className="colunas">
        {colunas.map(({ app, papel, comando }) => (
          <ColunaDeLogs
            key={app}
            app={app}
            titulo={nomeDoApp[app]}
            papel={papel}
            comandoParaSubir={comando}
            linhas={logs[app].linhas}
            estado={logs[app].estado}
            onLimpar={logs[app].limpar}
            trilha={trilha}
            idFixado={idFixado}
            onFixarId={fixar}
            controles={app === 'assinantes' ? <ControleDeAssinaturas versao={versao} /> : undefined}
            ativaNoCelular={colunaNoCelular === app}
          />
        ))}
      </main>
    </div>
  );
}
