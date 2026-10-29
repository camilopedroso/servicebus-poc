# POC Azure Service Bus — Emissão de apólice de seguro de vida

POC para estudar Azure Service Bus em C#/.NET 10, comparando com Kafka e RabbitMQ.

| Fase | Tema                                                                          | Status |
| ---- | ----------------------------------------------------------------------------- | ------ |
| 1    | Fila: peek-lock, Complete/Abandon/DeadLetter, retry, DLQ, competing consumers | ✅      |
| 2    | Tópico + assinaturas com filtros, transação entre entidades                   | ✅      |
| 3    | Sessions, detecção de duplicatas, mensagens agendadas, deferral               | ✅      |
| 4    | API REST + Outbox no SQL Server                                               | ✅      |
| 5    | Observabilidade (OpenTelemetry → App Insights) + testes                       | ✅      |
| 6    | (Opcional) Azure DevOps + Azure Functions                                     | descartada |
| 7    | Serviços por squad: eventos, Bicep por dono, contratos como pacote, inbox     | ✅      |

Notas de estudo: [fase 1 — filas](docs/fase-1-filas.md) · [fase 2 — tópicos](docs/fase-2-topicos.md) · [fase 3 — sessions](docs/fase-3-sessions.md) · [fase 4 — outbox](docs/fase-4-outbox.md) · [fase 5 — observabilidade e testes](docs/fase-5-observabilidade.md) · [fase 7 — serviços por squad](docs/fase-7-servicos-por-squad.md)

As notas das fases 1 a 5 descrevem o código da época (Producer, Consumer, Assinantes, fila `propostas`). A fase 7
reorganizou tudo em serviços por squad; o caminho está em [docs/plano-reestruturacao.md](docs/plano-reestruturacao.md).

## Estrutura

Um serviço por squad. Dentro da squad, `ProjectReference`; entre squads, pacote NuGet com versão (feed local).

```
infra/
  main.bicep               orquestra os módulos (o up.ps1 não muda)
  servicos/                quem publica cria o tópico (*.publica.bicep), quem consome cria a assinatura (*.assina.bicep):
                           plataforma (namespace, RBAC, App Insights), propostas, subscricao, cobranca, notificacao,
                           resseguro (assinatura alto-valor, sem serviço); padroes.bicep (propriedades das assinaturas)
  up.ps1 / down.ps1        cria/atualiza e apaga a infra no Azure
  sql-up.ps1 / sql-down.ps1  SQL Server 2022 local em Docker (porta 14330): bancos VidaPropostas e VidaSubscricao
src/
  Plataforma/              squad de Plataforma (Plataforma.slnx): Vida.Plataforma (ServiceBusClient, liga/desliga,
                           GET /assinaturas, leitura estrita de contratos), Vida.LogsAoVivo, Vida.Telemetria
  Operacao/Vida.Operacao   (:5184) peek, DLQ, reprocessamento, regras; descobre as entidades no broker
  Propostas/               Vida.Propostas (:5180): API REST + outbox + desfechos; Vida.Propostas.Contratos (pacote)
  Subscricao/              Vida.Subscricao (:5181): análise (inbox + outbox no banco dela), endossos (sessions),
                           demos de apólice e endosso; Vida.Subscricao.Contratos (pacote)
  Cobranca/Vida.Cobranca   (:5182) assinatura cobranca
  Notificacao/Vida.Notificacao  (:5183) assinaturas notificacao e notificacao-recusa
  Directory.Build.props    copia os *.local.json para os apps; controllers de biblioteca só por registro explícito
tests/<Squad>/             xUnit v3 por squad (Plataforma, Propostas + integração, Subscrição + integração)
web/painel/                React + Vite: cenários e uma coluna de logs ao vivo por serviço (lista em src/apps.ts)
publicar-pacotes.ps1       publica os pacotes compartilhados no feed local (pacotes/, fora do git)
nuget.config               feed local "vida" + package source mapping (Vida.* só dele)
ServiceBusPoc.slnx         todos os projetos, para estudar; cada squad também tem a sua .slnx
```

## Pré-requisitos

- .NET 10 SDK
- Node.js 20+ (para o painel)
- Docker Desktop (SQL Server de Propostas e da Subscrição; sem ele, a API de propostas responde 503 e a análise da
  Subscrição fica desligada, com as propostas esperando na fila)
- Azure CLI logado (`az login`) — o código autentica com `DefaultAzureCredential`, sem connection string

## Painel

Um comando sobe tudo e abre o navegador (Ctrl+C encerra; a saída de cada app fica em `logs\`):

```powershell
.\iniciar.ps1               # publica os pacotes se faltar algum, compila, sobe os cinco serviços e o painel
.\iniciar.ps1 -ComInfra     # antes, cria/atualiza a infra (infra\up.ps1)
.\iniciar.ps1 -ComInfra -ModoDebug   # idem, com lock de 5 min para depurar (ver "Modo debug")
.\iniciar.ps1 -Parar        # encerra processos que ficaram para trás (ex.: janela fechada no X)
# ou no prompt do Claude -> ! pwsh -File iniciar.ps1
```

Para depurar um serviço, suba-o pelo VS Code (configuração **Fluxo completo (todos os serviços)**) ou cada um no seu
terminal (depois de `.\publicar-pacotes.ps1`, se for a primeira vez):

```powershell
dotnet run --project src\Propostas\Vida.Propostas
dotnet run --project src\Subscricao\Vida.Subscricao
dotnet run --project src\Cobranca\Vida.Cobranca
dotnet run --project src\Notificacao\Vida.Notificacao
dotnet run --project src\Operacao\Vida.Operacao
cd web\painel; npm install; npm run dev     # http://localhost:5173
```

- **Cenário**: o dropdown lista os cenários das fases 1 a 4, cada um com seus parâmetros (idade, capital, plano,
  falha simulada, assinatura). Campo vazio = o serviço sorteia. Não é preciso reiniciar nada para trocar de cenário.
- **Colunas**: logs ao vivo de cada serviço (Propostas, Subscrição, Cobrança, Notificação, Operação). A barra
  "Colunas" esconde as que não interessam (os logs continuam chegando e entrando na trilha). Se um serviço cair, a
  coluna mostra o comando para subir de novo e reconecta sozinha.
- **Resseguro (sem serviço)**: cartão com quantas apólices esperam na assinatura `alto-valor`, que não tem consumidor.
- **Trilha**: clique em um id (proposta ou apólice) para destacar a mesma mensagem em todas as colunas. Linhas sem
  id de negócio (ex.: "Lote enviado com N mensagens") entram pelo TraceId da fase 5.
- **Selos**: cada coluna é um serviço; o selo diz de qual componente dele a linha veio (`API`, `outbox`, `cenário`
  em Propostas; `propostas`, `endossos`, `outbox` na Subscrição; a assinatura em Cobrança e Notificação).
- **Trace** (fase 5): no fim de cada linha, abre o trace no Aspire Dashboard (com `-ComAspire`) ou copia o TraceId
  para buscar no Application Insights.
- **Liga/desliga**: no cabeçalho das colunas de Cobrança e Notificação, cada assinatura (com pendentes); na da
  Subscrição, os processadores `propostas` e `endossos`.

Os endpoints também podem ser chamados direto (lista no início do `Program.cs` de cada serviço):

```powershell
curl -X POST localhost:5180/demo/propostas -H "Content-Type: application/json" -d '{\"idade\":85}'
curl localhost:5184/assinaturas/regras
curl localhost:5184/filas
```

## Custo

O tier Standard cobra **~US$ 0,013/hora (~US$ 10/mês)** enquanto o namespace existir, mesmo parado.
As primeiras 13 milhões de operações/mês estão inclusas. Por isso:

```powershell
.\infra\up.ps1     # ao começar a estudar (~1 min)
# ou no prompt do Claude -> ! pwsh -File infra/up.ps1

.\infra\down.ps1   # ao terminar
# ou no prompt do Claude -> ! pwsh -File infra/down.ps1
```

### Modo debug

Parado num breakpoint, o SDK não renova o lock. Com o lock normal de 30 s, a mensagem volta para a fila e o
`Complete` falha com `MessageLockLost` ("The lock supplied is invalid"). Para depurar com calma:

```powershell
.\infra\up.ps1 -ModoDebug   # lock de 5 min (máximo do Service Bus) na fila e nas assinaturas
# ou no prompt do Claude -> ! pwsh -File infra/up.ps1 -ModoDebug

.\infra\up.ps1              # volta para 30 s
# ou no prompt do Claude -> ! pwsh -File infra/up.ps1
```

Mesmo com 5 min, se ficar mais tempo que isso parado, o erro volta. Em produção o lock fica curto de propósito:
se um consumer travar, a mensagem volta logo para outro processar.

## Rodando a fase 1

Com os serviços e o painel no ar (seção **Painel**), use os cenários do grupo "Fase 1: tópico proposta-recebida →
fila subscricao-entrada". A primeira chamada
pode levar ~20 s enquanto o `DefaultAzureCredential` acha o login do Azure CLI.

| Cenário no painel                  | O que acontece                                                     |
| ---------------------------------- | ------------------------------------------------------------------ |
| Enviar lote de propostas válidas   | N propostas em batch; a Subscrição aprova cada uma                 |
| Enviar uma proposta sob medida     | idade, capital, plano e falha simulada escolhidos por você         |
| Proposta recusada por idade        | idade 85 → recusa publicada em `proposta-recusada`, cliente avisado |
| Falha transitória até a DLQ        | 5 entregas seguidas → DLQ (`MaxDeliveryCountExceeded`)             |
| Falha irrecuperável                | corpo ilegível → DLQ na 1ª entrega (`PayloadInvalido`)             |
| Espiar a fila / Espiar a DLQ       | peek, sem consumir; na DLQ mostra o motivo                         |
| Reprocessar a DLQ                  | devolve a DLQ para a fila (payload inválido fica)                  |

Experimentos sugeridos:

1. **Competing consumers**: suba uma segunda Subscrição em outra porta (`dotnet run --project src\Subscricao\Vida.Subscricao -- --urls http://localhost:5191`) e envie um lote de 20. Cada mensagem é processada por só uma delas (o log mostra a instância). O painel mostra a da porta 5181.
2. **Lock expirando**: pare o consumer com a mensagem em processamento (ou aumente o `Task.Delay` para além do `MaxAutoLockRenewalDuration`) e veja a mensagem voltar com `DeliveryCount` maior.
3. **Portal**: em portal.azure.com → namespace `sb-vida-poc-*` → fila `subscricao-entrada`, veja contagem de mensagens ativas/DLQ e use o **Service Bus Explorer**.

## Rodando a fase 2

Fluxo completo: proposta no tópico → fila da Subscrição → ela aprova e publica `ApoliceEmitida` → cada assinatura
recebe sua cópia.
No painel, o lote da fase 1 já percorre o fluxo inteiro. Para testar só os filtros, sem a fila, use o grupo
"Fase 2: tópico apolice-emitida":

| Cenário no painel                  | O que acontece                                                     |
| ---------------------------------- | ------------------------------------------------------------------ |
| Publicar apólice direto no tópico  | capital até 1 milhão → cobranca + notificacao                      |
| Publicar apólice de alto valor     | 1,5 milhão → notificacao + alto-valor (prioridade alta), sem cobrança |
| Espiar uma assinatura / sua DLQ    | peek na assinatura escolhida                                       |
| Ler as regras das assinaturas      | filtros e actions aplicados pelo broker                            |

Experimentos sugeridos:

1. **Assinante parado não perde nada**: no seletor da coluna da Notificação, desligue `notificacao`, publique apólices e veja as pendentes subirem. Religue e ela processa o acúmulo.
2. **Competing consumers numa assinatura**: suba uma segunda Cobrança (`dotnet run --project src\Cobranca\Vida.Cobranca -- --urls=http://localhost:5192`) e veja as apólices divididas entre as duas, enquanto `notificacao` continua recebendo todas.
3. **Nova assinatura**: crie uma no portal com filtro SQL `plano = 'Empresarial'`. Ela só recebe o que for publicado depois de criada.

## Rodando a fase 3

Grupo "Fase 3: sessions, duplicatas e agendamento". Os endossos vão para a fila `endossos` e aparecem na coluna da
Subscrição; clique no id de uma apólice para seguir só os endossos dela.

| Cenário no painel                           | O que acontece                                                          |
| ------------------------------------------- | ----------------------------------------------------------------------- |
| Endossos em ordem (sessions)                | 3 apólices × 4 versões intercaladas; cada apólice aplica v1→v4 em ordem |
| Endossos fora de ordem (deferral)           | 5 versões embaralhadas; adiantadas são adiadas e retomadas em sequência |
| Endosso duplicado (detecção de duplicatas)  | v1 enviada duas vezes com o mesmo MessageId; o broker descarta a cópia  |
| Falha transitória com backoff (agendamento) | tentativas agendadas a 2 s e 4 s; aprovada na 3ª                        |

Experimentos sugeridos:

1. **Sessões entre instâncias**: suba uma segunda Subscrição (`dotnet run --project src\Subscricao\Vida.Subscricao -- --urls http://localhost:5191`) e envie endossos em ordem com várias apólices. Cada apólice fica inteira numa instância de cada vez; as apólices se dividem entre as duas.
2. **Duplicata fora da janela**: a janela é de 10 min. Um reenvio depois disso passa pelo broker e é barrado pelo consumer (`versao <= UltimaVersao`): a idempotência no consumer continua necessária.
3. **Versão que nunca chega**: com a demo interrompida no meio de uma sequência, as versões posteriores ficam adiadas. Elas aparecem no peek do portal (Service Bus Explorer) com estado *Deferred*.

## Rodando a fase 4

O `iniciar.ps1` sobe o SQL Server em Docker antes dos apps (`infra\sql-up.ps1`, que também pode ser rodado sozinho).
Na primeira partida, Propostas cria o banco `VidaPropostas` e a Subscrição o `VidaSubscricao`, pelas migrations do
EF Core. Grupo "Fase 4: API REST +
outbox (SQL Server)":

| Cenário no painel                        | O que acontece                                                             |
| ---------------------------------------- | -------------------------------------------------------------------------- |
| Criar proposta pela API REST (outbox)    | 201 + Location; o despachante publica em ~1 s; GET mostra EnviadaParaAnalise |
| POST repetido com Idempotency-Key        | 201 e depois 200 com `Idempotent-Replayed: true`, mesmo id                 |
| Service Bus fora do ar (outbox acumula)  | a API segue respondendo 201; o outbox acumula com backoff                  |
| Restabelecer o Service Bus               | as pendentes saem na ordem de gravação                                     |
| Sem outbox: dual write com falha         | a proposta é gravada e nunca publicada (fica em Recebida)                  |
| Ver o outbox                             | pendentes e publicadas                                                     |

A API também pode ser chamada direto. Em Development, o contrato fica em `http://localhost:5180/openapi/v1.json` e a
interface para explorar e testar (Scalar) em `http://localhost:5180/scalar`:

```powershell
curl -i -X POST localhost:5180/api/propostas -H "Content-Type: application/json" -H "Idempotency-Key: abc-123" `
  -d '{\"clienteId\":\"CLI-1\",\"idade\":40,\"capitalSegurado\":300000,\"plano\":\"Familiar\"}'
```

Para olhar as tabelas: conecte em `localhost,14330` (usuário `sa`, senha em `src\sqlserver.local.json`) pelo
SSMS, Azure Data Studio ou a extensão SQL Server do VS Code.

## Rodando a fase 5

**Telemetria.** Depois do `up.ps1`, os três apps mandam traces, métricas e logs para o Application Insights
(autenticação Entra ID, sem chave). Os dados aparecem no portal em 2 a 5 minutos: Application Map, Transaction search
(busque pelo id da proposta) ou Logs (KQL, exemplos em [docs/fase-5-observabilidade.md](docs/fase-5-observabilidade.md)).
Para ver na hora, suba com o Aspire Dashboard local:

```powershell
.\iniciar.ps1 -ComAspire    # traces, logs e métricas em http://localhost:18888 (além do App Insights)
```

Um POST em `/api/propostas` (cenário "Criar proposta pela API REST") gera um trace só, do HTTP até as assinaturas.

**Testes.** Com o Docker rodando (os de integração sobem um SQL Server descartável):

```powershell
dotnet test                                                     # tudo, pela ServiceBusPoc.slnx
dotnet test --solution src\Subscricao\Subscricao.slnx          # só uma squad
dotnet test --project tests\Propostas\Vida.Propostas.Testes     # só unidade de Propostas, sem infraestrutura
```

## Rodando a fase 7

A reorganização em serviços por squad está em [docs/fase-7-servicos-por-squad.md](docs/fase-7-servicos-por-squad.md).
Cenários que mostram o que mudou:

| Cenário no painel                          | O que mostra                                                                 |
| ------------------------------------------ | ---------------------------------------------------------------------------- |
| Outbox com a Subscrição desligada          | a API e o outbox não dependem do consumidor; religou, a fila drena           |
| Queda entre gravar e concluir (inbox)      | a Subscrição grava e "cai"; a reentrega cai na inbox, uma apólice só        |
| Publicar apólice de alto valor             | a cópia de `alto-valor` espera: o cartão do Resseguro conta                  |
| Ler as regras das assinaturas              | a Operação descobre as assinaturas no broker, inclusive a que só encaminha  |

Contratos como pacote:

```powershell
.\publicar-pacotes.ps1      # publica no feed local o que ainda não está lá (o iniciar.ps1 chama sempre)
# mudou um contrato? suba a <Version> no csproj dele, publique, e atualize quem quiser a versão nova
```
