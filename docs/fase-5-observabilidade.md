# Fase 5 — Observabilidade (OpenTelemetry → Application Insights) e testes

## Um trace atravessando banco, fila e tópico

O mesmo `TraceId` liga a requisição HTTP ao processamento nos três apps. Trace real da POC, consultado no
Application Insights:

```
Vida.Producer    POST /api/propostas/                     296 ms
Vida.Producer      SQL: INSERT                              3 ms    (proposta + outbox, uma transação)
                 ── fila "propostas" ──
Vida.Consumer    ServiceBusProcessor.ProcessMessage       750 ms
Vida.Consumer      AnalisarProposta                       714 ms    (span de negócio: id, plano, resultado)
Vida.Consumer        ServiceBusReceiver.Complete / ServiceBusSender.Send   (transação Complete + Send)
                 ── tópico "apolice-emitida" ──
Vida.Assinantes  ServiceBusProcessor.ProcessMessage  ×2              (cobranca, notificacao)
```

O despachante do outbox aparece num trace **próprio** (`PublicarOutbox`): ele só transporta a mensagem.

## Como o contexto atravessa o Service Bus

O SDK carrega o contexto W3C (`traceparent`) na application property **`Diagnostic-Id`**, preenchida no envio.
O consumer lê a propriedade e continua o trace.

**O outbox quebra isso.** O envio acontece depois, dentro do span do despachante, então o SDK gravaria o contexto
do despachante, e o Consumer ficaria "filho" dele, não do POST. A correção (`MensagemDeSaida.De`) grava o
`Diagnostic-Id` **na hora do POST**, junto com a linha do outbox. O SDK não sobrescreve uma propriedade que já
existe. Verificado na POC pelo `TraceId` dos logs ao vivo: o mesmo na API, no Consumer e nas assinaturas; outro
no despachante.

## A distro e as decisões

| Decisão | Por quê |
|---|---|
| **Microsoft OpenTelemetry Distro** (`Microsoft.OpenTelemetry` 1.2.0) | É a recomendação atual da Microsoft para apps novos. A Azure Monitor Distro (`Azure.Monitor.OpenTelemetry.AspNetCore`) continua suportada para quem já usa. Uma chamada liga ASP.NET Core, HttpClient, SqlClient e Azure SDK |
| **Autenticação Entra ID na ingestão** | App Insights com `DisableLocalAuth: true` e role **Monitoring Metrics Publisher** (publica traces, logs e métricas, apesar do nome). A connection string vira só um endereço, como no Service Bus |
| **`Azure.Experimental.EnableActivitySource`** | O tracing do SDK do Service Bus via `ActivitySource` ainda é experimental. A distro liga a instrumentação do Azure SDK; o switch garante os spans do Service Bus |
| **`service.name` por app** | Vira o *cloud role name*: separa os três apps no Application Map |
| **Sem amostragem** | A distro limita a 5 traces/s por padrão. Na POC todo trace interessa; em produção, amostragem é controle de custo |
| **Teto diário de 0,1 GB no workspace** | Trava de custo da POC. O App Insights é cobrado pelo Log Analytics (por GB, 5 GB/mês grátis por conta de cobrança) |
| **Aspire Dashboard opcional** (`iniciar.ps1 -ComAspire`) | O App Insights leva minutos para mostrar um trace; o dashboard local (OTLP, Docker) mostra na hora. A distro exporta para os dois |

## Ruído e custo: o que fica fora do trace

Com amostragem desligada, todo span é cobrado. Três fontes de ruído apareceram e foram cortadas:

- **Polling do outbox**: uma consulta por segundo viraria 86 mil traces por dia. A consulta de pendentes roda dentro
  de `SuppressInstrumentationScope`; o span `PublicarOutbox` só nasce quando há o que publicar.
- **Rotas do painel**: o SSE de `/logs` e o polling de `GET /assinaturas` (a cada 15 s, ou 3 s com a lista
  aberta) passam por duas peças. O filtro da instrumentação do ASP.NET Core descarta o span da requisição.
  `SupressaoNasRotasDoPainel` (um `IStartupFilter`) roda essas rotas dentro de `SuppressInstrumentationScope`.
  O filtro sozinho não bastava: no workspace, 432 das 549 dependências do Assinantes eram
  `GetSubscriptionRuntimeProperties` e o `GET` HTTP por baixo, em operações sem o span pai. O filtro só marca a
  requisição como não gravada; o sampler da distro (`ApplicationInsightsSampler`) decide pelo `TraceId`, não herda
  a decisão do pai, e grava os filhos.
- **Migração na partida**: as consultas do EF (`__EFMigrationsHistory`, `CREATE TABLE`) ficam fora.

Sobram, por design do SDK, os spans `ServiceBusReceiver.Receive` (um por lote recebido, ligados à mensagem por
*link*, não como pai) e `DefaultAzureCredential.GetToken`.

## Spans e métricas de negócio

- `FonteDeAtividades` (`ActivitySource "Vida"`): `AnalisarProposta`, `ProcessarEndosso`, `PublicarOutbox`. As tags
  (`vida.proposta.id`, `vida.endosso.acao`...) viram *custom dimensions*: dá para achar o trace de uma proposta pelo
  id de negócio.
- `MetricasDeNegocio` (`Meter "Vida"`): `vida.propostas.decididas` (por resultado), `vida.outbox.atraso` (ms entre
  gravar e publicar), `vida.endossos.adiados`. São o tipo de coisa em que se alerta: atraso do outbox subindo =
  Service Bus com problema.
- Os logs do `ILogger` vão junto, com `TraceId`/`SpanId`: no App Insights, a linha de log aparece dentro do trace.
  Os logs ao vivo do painel também ganharam o `TraceId`.

### Os nomes não batem

| OpenTelemetry | App Insights (clássico) | Log Analytics (workspace-based, o da POC) |
|---|---|---|
| span de entrada (HTTP, `ProcessMessage`) | `requests` | `AppRequests` |
| span de saída (SQL, `Send`, `Complete`, spans de negócio) | `dependencies` | `AppDependencies` |
| **log do `ILogger`** | **`traces`** | **`AppTraces`** |
| métrica | `customMetrics` | `AppMetrics` |
| `TraceId` / `SpanId` | `operation_Id` / `id` | `OperationId` / `Id` (e `ParentId`) |
| `service.name` | `cloud_RoleName` | `AppRoleName` |

No App Insights, "trace" é **log**; a árvore de spans é a **operation**.

### Como um log chega (conferido no workspace)

- `Message`: o texto **já formatado** (a distro liga `IncludeFormattedMessage` e `ParseStateValues`).
- `SeverityLevel`: 0 Verbose, 1 Information, 2 Warning, 3 Error, 4 Critical.
- `Properties`: `CategoryName` (a classe que logou) e uma chave por placeholder (`PropostaId`, `ApoliceId`,
  `Plano`, `Idade`...). **Todos os valores chegam como texto**: `toint(Properties.Idade)` para comparar número.
- Tags dos spans de negócio também vão para `Properties`, com ponto no nome: `Properties['vida.apolice.id']`.
- Log sem span ativo (ex.: "Ouvindo a fila..." na partida) fica com `OperationId` vazio: existe, mas nunca aparece
  dentro de uma transação.
- `BeginScope` não é exportado pela distro (a POC não usa).

No portal: **Transaction search** (filtro *Trace* = logs; busca por id de negócio) → clicar abre o
**End-to-end transaction details**; o painel *Traces & events* mostra os logs intercalados com os spans dos três
apps. A ingestão leva 2 a 5 min; para ver na hora, `iniciar.ps1 -ComAspire`. `TimeGenerated` é UTC.

### Consultando (KQL)

```kusto
// Logs recentes, legíveis
AppTraces
| where TimeGenerated > ago(30m)
| extend Nivel = case(SeverityLevel == 0, "Verbose", SeverityLevel == 1, "Info",
                      SeverityLevel == 2, "Warning", SeverityLevel == 3, "Error", "Critical"),
         Classe = tostring(Properties.CategoryName)
| project TimeGenerated, AppRoleName, Nivel, Classe, Message, OperationId
| order by TimeGenerated desc

// O fluxo de uma proposta nos três apps: acha o trace pelo id de negócio
let id = '<propostaId>';
let operacoes = AppTraces | where tostring(Properties.PropostaId) == id | distinct OperationId;
union AppRequests, AppDependencies, AppTraces
| where OperationId in (operacoes)
| where Name !in ('Message', 'ServiceBusReceiver.Receive')   // spans do SDK sem valor para leitura
| extend O_que = coalesce(Message, Name)
| project TimeGenerated, AppRoleName, Type, O_que, DurationMs
| order by TimeGenerated asc
// Um POST em lote é um trace só: a consulta traz as outras propostas do lote junto
// (o mesmo motivo de a trilha do painel não puxar, pelo TraceId, linhas que têm id).

// Avisos e erros por app e classe
AppTraces
| where SeverityLevel >= 2
| summarize Quantidade = count() by AppRoleName, Classe = tostring(Properties.CategoryName)

// Atraso do outbox, por minuto
AppMetrics | where Name == "vida.outbox.atraso"
| summarize media = sum(Sum) / sum(ItemCount) by bin(TimeGenerated, 1m)
```

## Testes

`dotnet test` na raiz roda tudo (40 testes). O runner é o **Microsoft.Testing.Platform** (`global.json`): no SDK do
.NET 10, o xUnit v3 não roda mais pelo VSTest com `dotnet test`.

### Unidade (`tests/Vida.Testes.Unidade`, 31 testes, sem infraestrutura)
- Domínio: `PoliticaDeRevisao` (limite), `CalculadoraDePremio`.
- Contratos: a leitura estrita rejeita `{}`, camelCase, idade como texto e `Plano` nulo (o bug da fase 1).
- Mensagens: `MessageId` do endosso estável entre reenvios (base do dedup), `SessionId` = apólice, tipos das
  propriedades que os filtros SQL comparam (`requerRevisao` bool, `capitalSegurado` long).
- `DecisaoDeEndosso`: a regra de ordenação extraída do processador para uma **função pura** (aplicar, adiar,
  ignorar), incluindo a sequência embaralhada v2, v4, v1, v5, v3 terminando em v1 a v5.
- Outbox: ida e volta preserva ids e tipos; o `Diagnostic-Id` é o do momento da gravação.

### Integração (`tests/Vida.Testes.Integracao`, 9 testes, precisa de Docker)
- **SQL Server real** em container descartável (Testcontainers) e o Producer em memória (`WebApplicationFactory`).
- API: 201 + Location com proposta e linha do outbox gravadas juntas; 400 sem gravar nada; 404; `Idempotency-Key`
  repetida; **10 POSTs simultâneos com a mesma chave criam uma proposta só**.
- Despachante com um `ServiceBusClient` falso (o SDK tem construtores protegidos e métodos virtuais para isso):
  publica e marca; com o broker fora, a linha fica pendente com backoff; linha em backoff não é reenviada antes da
  hora.
- **Teste de mutação manual**: desligando o tratamento da violação do índice único, o teste dos POSTs simultâneos
  falha (9 de 10 recebem 500). O teste exercita de fato a corrida.

### Dois ajustes que os testes forçaram
- **Precedência de configuração**: os arquivos `*.local.json` eram carregados depois das variáveis de ambiente e da
  linha de comando, e venciam. Em produção, uma variável do App Service seria ignorada; nos testes, o
  `WebApplicationFactory` (que passa a config como linha de comando) usaria o banco de desenvolvimento e mandaria
  telemetria para o Azure. Agora ambiente e linha de comando são adicionados de novo depois dos arquivos.
- `DespachanteDoOutbox.DespacharLoteAsync` ficou `internal` (com `InternalsVisibleTo`) para o teste rodar um lote
  de forma determinística, sem esperar o timer.

## Perguntas para revisar

1. **Como rastrear uma mensagem do HTTP até o último consumidor?** W3C Trace Context propagado em
   `Diagnostic-Id`; OpenTelemetry nos três apps; App Insights (ou qualquer backend OTLP) junta pelo `OperationId`.
2. **E com outbox no meio?** Gravar o contexto junto com a mensagem, na hora da transação de negócio. Senão o trace
   começa no publicador.
3. **Por que OpenTelemetry e não o SDK do Application Insights?** Padrão aberto, sem acoplamento ao backend
   (troca o exportador, não a instrumentação), e é a direção da Microsoft (SDK 2.x em migração).
4. **Como controlar custo de telemetria?** Amostragem, filtros nas fontes de ruído (polling, health checks), teto
   diário no workspace, alertas de ingestão.
5. **O que testar num sistema com mensageria?** Regras puras em teste de unidade (extrair a decisão do handler);
   persistência e concorrência contra banco real (Testcontainers); o broker com fakes do SDK, ou com o emulador do
   Service Bus em Docker para testes de ponta a ponta.

## Referências

- [Habilitar OpenTelemetry (Microsoft OpenTelemetry Distro)](https://learn.microsoft.com/azure/azure-monitor/app/opentelemetry-enable?tabs=aspnetcore)
- [Autenticação Entra ID no Application Insights](https://learn.microsoft.com/azure/azure-monitor/app/azure-ad-authentication)
- [Tracing distribuído no Service Bus (Diagnostic-Id)](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-end-to-end-tracing)
- [Spans do SDK do Service Bus](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-troubleshooting-guide#distributed-tracing)
- [Preço do Application Insights](https://learn.microsoft.com/azure/azure-monitor/app/application-insights-faq#what-is-the-application-insights-pricing-model)
- [Testes de integração no ASP.NET Core](https://learn.microsoft.com/aspnet/core/test/integration-tests)
- [Microsoft.Testing.Platform com dotnet test](https://aka.ms/dotnet-test-mtp-error)
