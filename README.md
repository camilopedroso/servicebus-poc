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
| 8    | Sem demonstrações: backoff por falha real, API de endossos, fail fast         | ✅      |

**Como funciona, com diagramas e o roteiro de testes: [docs/como-funciona.md](docs/como-funciona.md).**
O caminho fase a fase está no histórico do git.

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
                           GET /assinaturas, leitura estrita de contratos), Vida.Telemetria
  Operacao/Vida.Operacao   (:5184) peek, DLQ, reprocessamento, regras; descobre as entidades no broker
  Propostas/               Vida.Propostas (:5180): API REST + outbox + desfechos; Vida.Propostas.Contratos (pacote)
  Subscricao/              Vida.Subscricao (:5181): análise (inbox + outbox no banco dela), API de apólices e endossos,
                           endossos (sessions); Vida.Subscricao.Contratos (pacote)
  Cobranca/Vida.Cobranca   (:5182) assinatura cobranca
  Notificacao/Vida.Notificacao  (:5183) assinaturas notificacao e notificacao-recusa
  Directory.Build.props    copia os *.local.json para os apps; controllers de biblioteca só por registro explícito
tests/<Squad>/             xUnit v3 por squad (Plataforma, Propostas + integração, Subscrição + integração)
requests/<squad>/*.http    chamadas prontas (REST Client do VS Code / arquivos .http do Visual Studio)
publicar-pacotes.ps1       publica os pacotes compartilhados no feed local (pacotes/, fora do git)
nuget.config               feed local "vida" + package source mapping (Vida.* só dele)
ServiceBusPoc.slnx         todos os projetos; cada squad também tem a sua .slnx
```

## Pré-requisitos

- .NET 10 SDK
- Docker Desktop (SQL Server de Propostas e da Subscrição, que não sobem sem banco; Testcontainers nos testes de
  integração; Aspire Dashboard opcional)
- Azure CLI logado (`az login`) — o código autentica com `DefaultAzureCredential`, sem connection string

## Subindo

Um comando sobe o SQL Server e os cinco serviços (Ctrl+C encerra; a saída de cada app fica em `logs\`):

```powershell
.\iniciar.ps1               # SQL Server, publica os pacotes se faltar algum, compila e sobe os cinco serviços
.\iniciar.ps1 -ComInfra     # antes, cria/atualiza a infra (infra\up.ps1)
.\iniciar.ps1 -ComInfra -ModoDebug   # idem, com lock de 5 min para depurar (ver "Modo debug")
.\iniciar.ps1 -ComAspire    # também exporta traces, logs e métricas para o Aspire Dashboard (http://localhost:18888)
.\iniciar.ps1 -Parar        # encerra processos que ficaram para trás (ex.: janela fechada no X)
# ou no prompt do Claude -> ! pwsh -File iniciar.ps1
```

Para depurar um serviço, suba-o pelo VS Code (configuração **Fluxo completo (todos os serviços)**) ou cada um no seu
terminal (depois de `.\infra\sql-up.ps1` e `.\publicar-pacotes.ps1`, se for a primeira vez):

```powershell
dotnet run --project src\Propostas\Vida.Propostas
dotnet run --project src\Subscricao\Vida.Subscricao
dotnet run --project src\Cobranca\Vida.Cobranca
dotnet run --project src\Notificacao\Vida.Notificacao
dotnet run --project src\Operacao\Vida.Operacao
```

Em Development, cada API tem o contrato em `/openapi/v1.json` e a interface Scalar em `/scalar`
(Propostas :5180, Subscrição :5181, Operação :5184). A lista de endpoints está no início do `Program.cs` de cada
serviço, e há chamadas prontas em `requests/`.

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

## Fluxos e testes

Os fluxos (proposta, análise, endossos), as garantias e um roteiro de 19 testes do tipo "pare X, chame Y, observe Z"
estão em [docs/como-funciona.md](docs/como-funciona.md#8-testes-recomendados). As chamadas prontas ficam em `requests/`.

## Telemetria e testes

**Telemetria.** Depois do `up.ps1`, os serviços mandam traces, métricas e logs para o Application Insights
(autenticação Entra ID, sem chave). Os dados aparecem no portal em 2 a 5 minutos: Application Map, Transaction search
(busque pelo id da proposta) ou Logs (KQL, exemplos em [docs/como-funciona.md](docs/como-funciona.md#7-observabilidade)).
Para ver na hora, `.\iniciar.ps1 -ComAspire`. Um `POST /api/propostas` gera um trace só, do HTTP até as assinaturas.

**Testes.** Com o Docker rodando (os de integração sobem um SQL Server descartável):

```powershell
dotnet test                                                     # tudo, pela ServiceBusPoc.slnx
dotnet test --solution src\Subscricao\Subscricao.slnx          # só uma squad
dotnet test --project tests\Propostas\Vida.Propostas.Testes     # só unidade de Propostas, sem infraestrutura
```

**Contratos como pacote.**

```powershell
.\publicar-pacotes.ps1      # publica no feed local o que ainda não está lá (o iniciar.ps1 chama sempre)
# mudou um contrato? suba a <Version> no csproj dele, publique, e atualize quem quiser a versão nova
```
